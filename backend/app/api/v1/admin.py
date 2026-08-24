"""System-administrator user and comment management endpoints."""

from __future__ import annotations

from fastapi import APIRouter, Depends, Query, status
from sqlalchemy import func, or_, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app.api.v1.deps import AuthContext, require_admin, require_admin_csrf
from app.core.errors import AppError
from app.core.logging import log_admin_action
from app.db.session import get_db
from app.models import Comment, User
from app.schemas.artifact import CommentStatus
from app.schemas.auth import (
    AdminResetPassword,
    AdminUserCreate,
    AdminUserPatch,
    UserListResponse,
    UserRead,
    UserRole,
)
from app.services.security import hash_password
from app.services.sessions import revoke_all_sessions, utcnow
from app.services import task_closure
from app.services.tasks import get_task, task_read
from app.schemas.task import TaskRead


router = APIRouter(prefix="/admin", tags=["admin"])


def _user_not_found() -> AppError:
    return AppError("USER_NOT_FOUND", "User not found", status_code=404)


@router.get("/users", response_model=UserListResponse, summary="List users")
def list_users(
    page: int = Query(1, ge=1),
    page_size: int = Query(20, ge=1, le=100),
    q: str | None = Query(None, max_length=100),
    _auth: AuthContext = Depends(require_admin),
    db: Session = Depends(get_db),
) -> UserListResponse:
    """Return a bounded, searchable user list for administrators."""

    statement = select(User)
    count_statement = select(func.count()).select_from(User)
    search = q.strip() if q else ""
    if search:
        pattern = f"%{search}%"
        condition = or_(User.username.ilike(pattern), User.display_name.ilike(pattern))
        statement = statement.where(condition)
        count_statement = count_statement.where(condition)

    total = int(db.scalar(count_statement) or 0)
    users = db.scalars(
        statement.order_by(User.id).offset((page - 1) * page_size).limit(page_size)
    ).all()
    return UserListResponse(items=[UserRead.model_validate(user) for user in users], page=page, page_size=page_size, total=total)


@router.post("/users", response_model=UserRead, status_code=status.HTTP_201_CREATED, summary="Create user")
def create_user(
    payload: AdminUserCreate,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> User:
    """Create an employee or another system administrator."""

    existing = db.scalar(select(User.id).where(User.username == payload.username))
    if existing is not None:
        raise AppError("USERNAME_TAKEN", "Username is already in use", status_code=409)

    user = User(
        username=payload.username,
        display_name=payload.display_name,
        password_hash=hash_password(payload.password),
        role=payload.role.value,
        is_active=True,
    )
    db.add(user)
    try:
        db.commit()
    except IntegrityError:
        db.rollback()
        raise AppError("USERNAME_TAKEN", "Username is already in use", status_code=409) from None
    db.refresh(user)
    log_admin_action(
        "user.create",
        actor_id=auth.user.id,
        target_type="user",
        target_id=user.id,
        username=user.username,
        role=user.role,
    )
    return user


@router.patch("/users/{user_id}", response_model=UserRead, summary="Update user")
def update_user(
    user_id: int,
    payload: AdminUserPatch,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> User:
    """Update mutable user fields while protecting the current administrator."""

    user = db.get(User, user_id)
    if user is None:
        raise _user_not_found()

    changes = payload.model_dump(exclude_unset=True)
    if not changes:
        return user
    if user.id == auth.user.id:
        if changes.get("is_active") is False or changes.get("role") == UserRole.EMPLOYEE:
            raise AppError(
                "SELF_ADMIN_PROTECTED",
                "An administrator cannot deactivate or downgrade their own account",
                status_code=403,
            )

    if "display_name" in changes:
        user.display_name = changes["display_name"]
    if "role" in changes and changes["role"] is not None:
        role = changes["role"]
        user.role = role.value if isinstance(role, UserRole) else str(role)
    if "is_active" in changes and changes["is_active"] is not None:
        user.is_active = bool(changes["is_active"])
        if not user.is_active:
            revoke_all_sessions(db, user.id)
    user.updated_at = utcnow()
    db.commit()
    db.refresh(user)
    log_admin_action(
        "user.update",
        actor_id=auth.user.id,
        target_type="user",
        target_id=user.id,
        username=user.username,
        fields=sorted(changes),
    )
    return user


@router.post(
    "/users/{user_id}/reset-password",
    status_code=status.HTTP_204_NO_CONTENT,
    summary="Reset user password",
)
def reset_password(
    user_id: int,
    payload: AdminResetPassword,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> None:
    """Reset a user's password and revoke every session for that user."""

    user = db.get(User, user_id)
    if user is None:
        raise _user_not_found()
    user.password_hash = hash_password(payload.new_password)
    user.updated_at = utcnow()
    revoke_all_sessions(db, user.id)
    db.commit()
    log_admin_action(
        "user.reset_password",
        actor_id=auth.user.id,
        target_type="user",
        target_id=user.id,
        username=user.username,
    )


def _get_comment(db: Session, comment_id: int) -> Comment:
    comment = db.get(Comment, comment_id)
    if comment is None:
        raise AppError("COMMENT_NOT_FOUND", "Comment not found", status_code=404)
    return comment


def _set_comment_status(
    db: Session,
    comment: Comment,
    target_status: CommentStatus,
) -> None:
    if comment.status != target_status.value:
        comment.status = target_status.value
        comment.updated_at = utcnow()
        db.commit()


@router.post(
    "/comments/{comment_id}/hide",
    status_code=status.HTTP_204_NO_CONTENT,
    summary="Hide comment",
)
def hide_comment(
    comment_id: int,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> None:
    """Hide an inappropriate comment from non-administrator employees."""

    comment = _get_comment(db, comment_id)
    _set_comment_status(db, comment, CommentStatus.HIDDEN)
    log_admin_action(
        "comment.hide",
        actor_id=auth.user.id,
        target_type="comment",
        target_id=comment.id,
    )


@router.post(
    "/comments/{comment_id}/restore",
    status_code=status.HTTP_204_NO_CONTENT,
    summary="Restore comment",
)
def restore_comment(
    comment_id: int,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> None:
    """Make a previously hidden comment visible again."""

    comment = _get_comment(db, comment_id)
    _set_comment_status(db, comment, CommentStatus.VISIBLE)
    log_admin_action(
        "comment.restore",
        actor_id=auth.user.id,
        target_type="comment",
        target_id=comment.id,
    )


@router.post("/tasks/{task_id}/reopen", response_model=TaskRead, summary="Reopen closed task")
def reopen_task(
    task_id: int,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> TaskRead:
    """Reopen a closed task for correction; completed tasks stay terminal."""

    task = get_task(db, task_id)
    task_closure.reopen_task(db, task)
    log_admin_action(
        "task.reopen",
        actor_id=auth.user.id,
        target_type="task",
        target_id=task.id,
        status=task.status,
    )
    return task_read(db, task, viewer=auth.user)
