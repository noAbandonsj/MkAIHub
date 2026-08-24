"""Decision actions on task submissions (revision, accept, reject)."""

from __future__ import annotations

from fastapi import APIRouter, Depends
from sqlalchemy.orm import Session

from app.api.v1.deps import AuthContext, require_csrf
from app.core.logging import log_admin_action
from app.db.session import get_db
from app.schemas.auth import UserRole
from app.schemas.task import (
    SubmissionAcceptRequest,
    SubmissionRejectRequest,
    SubmissionRevisionRequest,
    TaskSubmissionRead,
)
from app.services import task_closure


router = APIRouter(prefix="/task-submissions", tags=["task-submissions"])


def _log_admin_decision(action: str, submission, user) -> None:
    """Audit administrators deciding submissions they did not publish."""

    if user.role == UserRole.SYSTEM_ADMIN.value and submission.task.creator_id != user.id:
        log_admin_action(
            f"task.submission_{action}",
            actor_id=user.id,
            target_type="task_submission",
            target_id=submission.id,
            task_id=submission.task_id,
        )


@router.post("/{submission_id}/request-revision", response_model=TaskSubmissionRead, summary="Request submission revision")
def request_revision(
    submission_id: int,
    payload: SubmissionRevisionRequest,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> TaskSubmissionRead:
    submission = task_closure.get_submission(db, submission_id)
    updated = task_closure.decide_submission(db, submission, auth.user, "request_revision", payload.note)
    _log_admin_decision("request_revision", updated, auth.user)
    return task_closure.submission_read(updated)


@router.post("/{submission_id}/accept", response_model=TaskSubmissionRead, summary="Accept submission")
def accept_submission(
    submission_id: int,
    payload: SubmissionAcceptRequest | None = None,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> TaskSubmissionRead:
    submission = task_closure.get_submission(db, submission_id)
    note = payload.note if payload is not None else None
    updated = task_closure.decide_submission(db, submission, auth.user, "accept", note)
    _log_admin_decision("accept", updated, auth.user)
    return task_closure.submission_read(updated)


@router.post("/{submission_id}/reject", response_model=TaskSubmissionRead, summary="Reject submission")
def reject_submission(
    submission_id: int,
    payload: SubmissionRejectRequest,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> TaskSubmissionRead:
    submission = task_closure.get_submission(db, submission_id)
    updated = task_closure.decide_submission(db, submission, auth.user, "reject", payload.note)
    _log_admin_decision("reject", updated, auth.user)
    return task_closure.submission_read(updated)
