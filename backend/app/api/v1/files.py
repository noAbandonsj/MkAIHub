"""Authenticated local attachment upload and download endpoints."""

from __future__ import annotations

from fastapi import APIRouter, Depends, File, Request, UploadFile, status
from fastapi.responses import FileResponse
from sqlalchemy import func, select
from sqlalchemy.orm import Session

from app.api.v1.deps import AuthContext, get_current_auth, require_csrf
from app.core.errors import AppError
from app.db.session import get_db
from app.models import Artifact, ArtifactFile, StoredFile
from app.schemas.artifact import FileRead
from app.schemas.auth import UserRole
from app.services.storage import save_upload, stored_path


router = APIRouter(prefix="/files", tags=["files"])


@router.post("", response_model=FileRead, status_code=status.HTTP_201_CREATED, summary="Upload file")
def upload_file(
    request: Request,
    upload: UploadFile = File(alias="file"),
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> StoredFile:
    saved = save_upload(upload, request.app.state.settings)
    stored_file = StoredFile(
        original_name=saved.original_name,
        stored_name=saved.stored_name,
        relative_path=saved.relative_path,
        extension=saved.extension,
        mime_type=saved.mime_type,
        size_bytes=saved.size_bytes,
        sha256=saved.sha256,
        uploader_id=auth.user.id,
        is_deleted=False,
    )
    db.add(stored_file)
    try:
        db.commit()
    except Exception:
        db.rollback()
        stored_path(saved.relative_path, request.app.state.settings).unlink(missing_ok=True)
        raise
    db.refresh(stored_file)
    return stored_file


@router.get("/{file_id}/download", summary="Download file")
def download_file(
    file_id: int,
    request: Request,
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> FileResponse:
    stored_file = db.get(StoredFile, file_id)
    if stored_file is None or stored_file.is_deleted:
        raise AppError("FILE_NOT_FOUND", "File not found", status_code=404)

    can_download = stored_file.uploader_id == auth.user.id or auth.user.role == UserRole.SYSTEM_ADMIN.value
    if not can_download:
        can_download = (
            db.scalar(
                select(func.count())
                .select_from(ArtifactFile)
                .join(Artifact, Artifact.id == ArtifactFile.artifact_id)
                .where(
                    ArtifactFile.file_id == stored_file.id,
                    Artifact.status == "PUBLISHED",
                )
            )
            or 0
        ) > 0
    if not can_download:
        raise AppError("FILE_NOT_FOUND", "File not found", status_code=404)

    path = stored_path(stored_file.relative_path, request.app.state.settings)
    if not path.is_file():
        raise AppError("FILE_CONTENT_MISSING", "Stored file is missing", status_code=404)
    return FileResponse(
        path,
        media_type=stored_file.mime_type,
        filename=stored_file.original_name,
        content_disposition_type="attachment",
    )


@router.delete("/{file_id}", status_code=status.HTTP_204_NO_CONTENT, summary="Delete unreferenced upload")
def delete_file(
    file_id: int,
    request: Request,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> None:
    stored_file = db.get(StoredFile, file_id)
    if stored_file is None or stored_file.is_deleted:
        raise AppError("FILE_NOT_FOUND", "File not found", status_code=404)
    if stored_file.uploader_id != auth.user.id:
        raise AppError("FORBIDDEN", "Only the uploader can delete this file", status_code=403)
    reference_count = int(
        db.scalar(
            select(func.count()).select_from(ArtifactFile).where(ArtifactFile.file_id == stored_file.id)
        )
        or 0
    )
    if reference_count:
        raise AppError("FILE_IN_USE", "A referenced file cannot be deleted", status_code=409)
    stored_file.is_deleted = True
    db.commit()
    stored_path(stored_file.relative_path, request.app.state.settings).unlink(missing_ok=True)
