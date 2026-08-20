"""Artifact CRUD, state actions, and artifact comments."""

from __future__ import annotations

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, Query, status
from sqlalchemy import func, or_, select
from sqlalchemy.orm import Session, joinedload

from app.api.v1.deps import AuthContext, get_current_auth, require_csrf
from app.core.errors import AppError
from app.db.session import get_db
from app.models import Artifact, Comment
from app.schemas.artifact import (
    ArtifactCreate,
    ArtifactListResponse,
    ArtifactRead,
    ArtifactStatus,
    ArtifactUpdate,
    CommentCreate,
    CommentListResponse,
    CommentRead,
    CommentStatus,
)
from app.schemas.auth import UserRole
from app.services.artifacts import (
    artifact_list_item,
    artifact_load_options,
    artifact_read,
    get_visible_artifact,
    replace_artifact_files,
    require_artifact_author,
    require_author_or_admin,
)
from app.services.comments import comment_read


router = APIRouter(prefix="/artifacts", tags=["artifacts"])
comment_router = APIRouter(tags=["comments"])


def _state_conflict(message: str) -> AppError:
    return AppError("ARTIFACT_STATE_CONFLICT", message, status_code=409)


@router.get("", response_model=ArtifactListResponse, summary="List artifacts")
def list_artifacts(
    page: int = Query(1, ge=1),
    page_size: int = Query(12, ge=1, le=100),
    q: str | None = Query(None, max_length=100),
    mine: bool = False,
    status_filter: ArtifactStatus | None = Query(None, alias="status"),
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> ArtifactListResponse:
    conditions = []
    if mine:
        conditions.append(Artifact.author_id == auth.user.id)
    elif status_filter is not None and auth.user.role == UserRole.SYSTEM_ADMIN.value:
        # Administrators maintain archived content, so an explicit status
        # filter replaces the default published-only scope for them.
        conditions.append(Artifact.status == status_filter.value)
    else:
        conditions.append(Artifact.status == ArtifactStatus.PUBLISHED.value)
        if status_filter is not None:
            conditions.append(Artifact.status == status_filter.value)
    search = q.strip() if q else ""
    if search:
        pattern = f"%{search}%"
        conditions.append(or_(Artifact.title.ilike(pattern), Artifact.summary.ilike(pattern)))

    count_statement = select(func.count()).select_from(Artifact).where(*conditions)
    ordering = (
        (Artifact.updated_at.desc(), Artifact.id.desc())
        if mine
        else (Artifact.published_at.desc(), Artifact.id.desc())
    )
    statement = (
        select(Artifact)
        .options(*artifact_load_options())
        .where(*conditions)
        .order_by(*ordering)
        .offset((page - 1) * page_size)
        .limit(page_size)
    )
    total = int(db.scalar(count_statement) or 0)
    artifacts = list(db.scalars(statement).all())
    return ArtifactListResponse(
        items=[artifact_list_item(item) for item in artifacts],
        page=page,
        page_size=page_size,
        total=total,
    )


@router.post("", response_model=ArtifactRead, status_code=status.HTTP_201_CREATED, summary="Create artifact draft")
def create_artifact(
    payload: ArtifactCreate,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> ArtifactRead:
    artifact = Artifact(
        title=payload.title,
        summary=payload.summary,
        content_markdown=payload.content_markdown,
        author_id=auth.user.id,
        status=ArtifactStatus.DRAFT.value,
    )
    replace_artifact_files(db, artifact, payload.file_ids, auth.user)
    db.add(artifact)
    db.commit()
    return artifact_read(get_visible_artifact(db, artifact.id, auth.user))


@router.get("/{artifact_id}", response_model=ArtifactRead, summary="Get artifact")
def get_artifact(
    artifact_id: int,
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> ArtifactRead:
    return artifact_read(get_visible_artifact(db, artifact_id, auth.user))


@router.patch("/{artifact_id}", response_model=ArtifactRead, summary="Update artifact")
def update_artifact(
    artifact_id: int,
    payload: ArtifactUpdate,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> ArtifactRead:
    artifact = get_visible_artifact(db, artifact_id, auth.user)
    require_artifact_author(artifact, auth.user)
    if artifact.status == ArtifactStatus.ARCHIVED.value:
        raise _state_conflict("Restore an archived artifact before editing it")

    changes = payload.model_dump(exclude_unset=True)
    if any(value is None for value in changes.values()):
        raise AppError("ARTIFACT_FIELD_REQUIRED", "Artifact fields cannot be null", status_code=422)
    for field in ("title", "summary", "content_markdown"):
        if field in changes:
            setattr(artifact, field, changes[field])
    if "file_ids" in changes:
        replace_artifact_files(db, artifact, changes["file_ids"], auth.user)
    if changes:
        artifact.updated_at = datetime.now(UTC)
        db.commit()
    return artifact_read(get_visible_artifact(db, artifact.id, auth.user))


@router.delete("/{artifact_id}", status_code=status.HTTP_204_NO_CONTENT, summary="Delete artifact draft")
def delete_artifact(
    artifact_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> None:
    artifact = get_visible_artifact(db, artifact_id, auth.user)
    require_artifact_author(artifact, auth.user)
    if artifact.status != ArtifactStatus.DRAFT.value:
        raise _state_conflict("Only a draft artifact can be deleted")
    db.delete(artifact)
    db.commit()


@router.post("/{artifact_id}/publish", response_model=ArtifactRead, summary="Publish artifact")
def publish_artifact(
    artifact_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> ArtifactRead:
    artifact = get_visible_artifact(db, artifact_id, auth.user)
    require_artifact_author(artifact, auth.user)
    if artifact.status != ArtifactStatus.DRAFT.value:
        raise _state_conflict("Only a draft artifact can be published")
    now = datetime.now(UTC)
    artifact.status = ArtifactStatus.PUBLISHED.value
    artifact.published_at = now
    artifact.archived_at = None
    artifact.updated_at = now
    db.commit()
    return artifact_read(get_visible_artifact(db, artifact.id, auth.user))


@router.post("/{artifact_id}/archive", response_model=ArtifactRead, summary="Archive artifact")
def archive_artifact(
    artifact_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> ArtifactRead:
    artifact = get_visible_artifact(db, artifact_id, auth.user)
    require_author_or_admin(artifact, auth.user)
    if artifact.status != ArtifactStatus.PUBLISHED.value:
        raise _state_conflict("Only a published artifact can be archived")
    now = datetime.now(UTC)
    artifact.status = ArtifactStatus.ARCHIVED.value
    artifact.archived_at = now
    artifact.updated_at = now
    db.commit()
    return artifact_read(get_visible_artifact(db, artifact.id, auth.user))


@router.post("/{artifact_id}/restore", response_model=ArtifactRead, summary="Restore artifact")
def restore_artifact(
    artifact_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> ArtifactRead:
    artifact = get_visible_artifact(db, artifact_id, auth.user)
    require_author_or_admin(artifact, auth.user)
    if artifact.status != ArtifactStatus.ARCHIVED.value:
        raise _state_conflict("Only an archived artifact can be restored")
    artifact.status = ArtifactStatus.PUBLISHED.value
    artifact.archived_at = None
    artifact.updated_at = datetime.now(UTC)
    db.commit()
    return artifact_read(get_visible_artifact(db, artifact.id, auth.user))


@router.get("/{artifact_id}/comments", response_model=CommentListResponse, summary="List artifact comments")
def list_comments(
    artifact_id: int,
    page: int = Query(1, ge=1),
    page_size: int = Query(50, ge=1, le=100),
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> CommentListResponse:
    get_visible_artifact(db, artifact_id, auth.user)
    conditions = [Comment.artifact_id == artifact_id]
    # Hidden comments stay visible to administrators so they can restore them.
    if auth.user.role != UserRole.SYSTEM_ADMIN.value:
        conditions.append(Comment.status == CommentStatus.VISIBLE.value)
    conditions = tuple(conditions)
    total = int(db.scalar(select(func.count()).select_from(Comment).where(*conditions)) or 0)
    comments = list(
        db.scalars(
            select(Comment)
            .options(joinedload(Comment.author))
            .where(*conditions)
            .order_by(Comment.created_at.asc(), Comment.id.asc())
            .offset((page - 1) * page_size)
            .limit(page_size)
        ).all()
    )
    return CommentListResponse(
        items=[comment_read(item) for item in comments],
        page=page,
        page_size=page_size,
        total=total,
    )


@router.post("/{artifact_id}/comments", response_model=CommentRead, status_code=status.HTTP_201_CREATED, summary="Comment on artifact")
def create_comment(
    artifact_id: int,
    payload: CommentCreate,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> CommentRead:
    artifact = get_visible_artifact(db, artifact_id, auth.user)
    if artifact.status != ArtifactStatus.PUBLISHED.value:
        raise _state_conflict("Only a published artifact can receive comments")
    comment = Comment(
        artifact_id=artifact.id,
        author_id=auth.user.id,
        content=payload.content,
        status="VISIBLE",
    )
    db.add(comment)
    db.commit()
    db.refresh(comment)
    comment.author = auth.user
    return comment_read(comment)


@comment_router.delete("/comments/{comment_id}", status_code=status.HTTP_204_NO_CONTENT, summary="Delete own comment")
def delete_comment(
    comment_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> None:
    comment = db.get(Comment, comment_id)
    if comment is None:
        raise AppError("COMMENT_NOT_FOUND", "Comment not found", status_code=404)
    if comment.author_id != auth.user.id:
        raise AppError("FORBIDDEN", "Only the comment author can delete it", status_code=403)
    db.delete(comment)
    db.commit()
