"""Issue CRUD, state actions, and issue comments."""

from __future__ import annotations

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, Query, status
from sqlalchemy import func, or_, select
from sqlalchemy.orm import Session, joinedload

from app.api.v1.deps import AuthContext, get_current_auth, require_csrf
from app.core.errors import AppError
from app.db.session import get_db
from app.models import Comment, Issue
from app.schemas.artifact import CommentCreate, CommentListResponse, CommentRead, CommentStatus
from app.schemas.auth import UserRole
from app.schemas.issue import (
    IssueCreate,
    IssueListResponse,
    IssueRead,
    IssueStatus,
    IssueUpdate,
)
from app.services.comments import comment_read
from app.services.issues import (
    get_issue,
    issue_list_item,
    issue_read,
    require_issue_author,
    require_issue_author_or_admin,
)


router = APIRouter(prefix="/issues", tags=["issues"])


def _state_conflict(message: str) -> AppError:
    return AppError("ISSUE_STATE_CONFLICT", message, status_code=409)


@router.get("", response_model=IssueListResponse, summary="List issues")
def list_issues(
    page: int = Query(1, ge=1),
    page_size: int = Query(12, ge=1, le=100),
    q: str | None = Query(None, max_length=100),
    mine: bool = False,
    status_filter: IssueStatus | None = Query(None, alias="status"),
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> IssueListResponse:
    conditions = []
    if mine:
        conditions.append(Issue.author_id == auth.user.id)
    if status_filter is not None:
        conditions.append(Issue.status == status_filter.value)
    search = q.strip() if q else ""
    if search:
        pattern = f"%{search}%"
        conditions.append(or_(Issue.title.ilike(pattern), Issue.description.ilike(pattern)))

    count_statement = select(func.count()).select_from(Issue).where(*conditions)
    statement = (
        select(Issue)
        .options(joinedload(Issue.author))
        .where(*conditions)
        .order_by(Issue.updated_at.desc(), Issue.id.desc())
        .offset((page - 1) * page_size)
        .limit(page_size)
    )
    total = int(db.scalar(count_statement) or 0)
    issues = list(db.scalars(statement).all())
    return IssueListResponse(
        items=[issue_list_item(item) for item in issues],
        page=page,
        page_size=page_size,
        total=total,
    )


@router.post("", response_model=IssueRead, status_code=status.HTTP_201_CREATED, summary="Create issue")
def create_issue(
    payload: IssueCreate,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> IssueRead:
    issue = Issue(
        title=payload.title,
        description=payload.description,
        author_id=auth.user.id,
        status=IssueStatus.OPEN.value,
    )
    db.add(issue)
    db.commit()
    return issue_read(get_issue(db, issue.id))


@router.get("/{issue_id}", response_model=IssueRead, summary="Get issue")
def get_issue_endpoint(
    issue_id: int,
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> IssueRead:
    return issue_read(get_issue(db, issue_id))


@router.patch("/{issue_id}", response_model=IssueRead, summary="Update issue")
def update_issue(
    issue_id: int,
    payload: IssueUpdate,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> IssueRead:
    issue = get_issue(db, issue_id)
    require_issue_author(issue, auth.user)
    if issue.status != IssueStatus.OPEN.value:
        raise _state_conflict("Reopen the issue before editing it")

    changes = payload.model_dump(exclude_unset=True)
    if any(value is None for value in changes.values()):
        raise AppError("ISSUE_FIELD_REQUIRED", "Issue fields cannot be null", status_code=422)
    for field in ("title", "description"):
        if field in changes:
            setattr(issue, field, changes[field])
    if changes:
        issue.updated_at = datetime.now(UTC)
        db.commit()
    return issue_read(get_issue(db, issue.id))


@router.post("/{issue_id}/close", response_model=IssueRead, summary="Close issue")
def close_issue(
    issue_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> IssueRead:
    issue = get_issue(db, issue_id)
    require_issue_author_or_admin(issue, auth.user)
    if issue.status != IssueStatus.OPEN.value:
        raise _state_conflict("Only an open issue can be closed")
    now = datetime.now(UTC)
    issue.status = IssueStatus.CLOSED.value
    issue.closed_at = now
    issue.updated_at = now
    db.commit()
    return issue_read(get_issue(db, issue.id))


@router.post("/{issue_id}/reopen", response_model=IssueRead, summary="Reopen issue")
def reopen_issue(
    issue_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> IssueRead:
    issue = get_issue(db, issue_id)
    require_issue_author_or_admin(issue, auth.user)
    if issue.status != IssueStatus.CLOSED.value:
        raise _state_conflict("Only a closed issue can be reopened")
    issue.status = IssueStatus.OPEN.value
    issue.closed_at = None
    issue.updated_at = datetime.now(UTC)
    db.commit()
    return issue_read(get_issue(db, issue.id))


@router.get("/{issue_id}/comments", response_model=CommentListResponse, summary="List issue comments")
def list_issue_comments(
    issue_id: int,
    page: int = Query(1, ge=1),
    page_size: int = Query(50, ge=1, le=100),
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> CommentListResponse:
    get_issue(db, issue_id)
    conditions = [Comment.issue_id == issue_id]
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


@router.post("/{issue_id}/comments", response_model=CommentRead, status_code=status.HTTP_201_CREATED, summary="Comment on issue")
def create_issue_comment(
    issue_id: int,
    payload: CommentCreate,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> CommentRead:
    issue = get_issue(db, issue_id)
    if issue.status != IssueStatus.OPEN.value:
        raise _state_conflict("Only an open issue can receive comments")
    comment = Comment(
        issue_id=issue.id,
        author_id=auth.user.id,
        content=payload.content,
        status="VISIBLE",
    )
    db.add(comment)
    db.commit()
    db.refresh(comment)
    comment.author = auth.user
    return comment_read(comment)
