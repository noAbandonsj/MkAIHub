"""Issue query, authorization, and response helpers."""

from __future__ import annotations

from sqlalchemy import select
from sqlalchemy.orm import Session, joinedload

from app.core.errors import AppError
from app.models import Issue, User
from app.schemas.artifact import UserSummary
from app.schemas.auth import UserRole
from app.schemas.issue import IssueListItem, IssueRead


def get_issue(db: Session, issue_id: int) -> Issue:
    issue = db.scalar(
        select(Issue).options(joinedload(Issue.author)).where(Issue.id == issue_id)
    )
    if issue is None:
        raise AppError("ISSUE_NOT_FOUND", "Issue not found", status_code=404)
    return issue


def require_issue_author(issue: Issue, user: User) -> None:
    if issue.author_id != user.id:
        raise AppError("FORBIDDEN", "Only the issue author can edit this issue", status_code=403)


def require_issue_author_or_admin(issue: Issue, user: User) -> None:
    if issue.author_id != user.id and user.role != UserRole.SYSTEM_ADMIN.value:
        raise AppError("FORBIDDEN", "Issue permission is required", status_code=403)


def issue_list_item(issue: Issue) -> IssueListItem:
    return IssueListItem(
        id=issue.id,
        title=issue.title,
        author=UserSummary.model_validate(issue.author),
        status=issue.status,
        closed_at=issue.closed_at,
        created_at=issue.created_at,
        updated_at=issue.updated_at,
    )


def issue_read(issue: Issue) -> IssueRead:
    return IssueRead(
        **issue_list_item(issue).model_dump(),
        description=issue.description,
    )
