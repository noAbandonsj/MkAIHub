"""Artifact query, authorization, and response helpers."""

from __future__ import annotations

from sqlalchemy import select
from sqlalchemy.orm import Session, joinedload, selectinload

from app.core.errors import AppError
from app.models import Artifact, ArtifactFile, StoredFile, User
from app.schemas.artifact import ArtifactListItem, ArtifactRead, FileRead, UserSummary
from app.schemas.auth import UserRole


def artifact_load_options():
    return (
        joinedload(Artifact.author),
        selectinload(Artifact.file_links).joinedload(ArtifactFile.file),
    )


def get_visible_artifact(db: Session, artifact_id: int, user: User) -> Artifact:
    artifact = db.scalar(
        select(Artifact).options(*artifact_load_options()).where(Artifact.id == artifact_id)
    )
    if artifact is None:
        raise AppError("ARTIFACT_NOT_FOUND", "Artifact not found", status_code=404)
    if (
        artifact.status != "PUBLISHED"
        and user.id != artifact.author_id
        and user.role != UserRole.SYSTEM_ADMIN.value
    ):
        raise AppError("ARTIFACT_NOT_FOUND", "Artifact not found", status_code=404)
    return artifact


def require_artifact_author(artifact: Artifact, user: User) -> None:
    if artifact.author_id != user.id:
        raise AppError("FORBIDDEN", "Only the artifact author can edit this content", status_code=403)


def require_author_or_admin(artifact: Artifact, user: User) -> None:
    if artifact.author_id != user.id and user.role != UserRole.SYSTEM_ADMIN.value:
        raise AppError("FORBIDDEN", "Artifact permission is required", status_code=403)


def replace_artifact_files(
    db: Session,
    artifact: Artifact,
    file_ids: list[int],
    user: User,
) -> None:
    files = (
        list(
            db.scalars(
                select(StoredFile).where(
                    StoredFile.id.in_(file_ids),
                    StoredFile.is_deleted.is_(False),
                )
            ).all()
        )
        if file_ids
        else []
    )
    by_id = {item.id: item for item in files}
    if len(by_id) != len(file_ids):
        raise AppError("FILE_NOT_FOUND", "One or more files were not found", status_code=404)
    if any(item.uploader_id != user.id for item in files):
        raise AppError("FILE_FORBIDDEN", "Only your own uploads can be attached", status_code=403)

    artifact.file_links.clear()
    for sort_order, file_id in enumerate(file_ids):
        artifact.file_links.append(ArtifactFile(file=by_id[file_id], sort_order=sort_order))


def artifact_list_item(artifact: Artifact) -> ArtifactListItem:
    return ArtifactListItem(
        id=artifact.id,
        title=artifact.title,
        summary=artifact.summary,
        author=UserSummary.model_validate(artifact.author),
        status=artifact.status,
        attachment_count=len(artifact.file_links),
        published_at=artifact.published_at,
        created_at=artifact.created_at,
        updated_at=artifact.updated_at,
    )


def artifact_read(artifact: Artifact) -> ArtifactRead:
    return ArtifactRead(
        **artifact_list_item(artifact).model_dump(),
        content_markdown=artifact.content_markdown,
        archived_at=artifact.archived_at,
        files=[
            FileRead.model_validate(link.file)
            for link in artifact.file_links
            if not link.file.is_deleted
        ],
    )
