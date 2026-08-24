"""Task participation, submission, and decision services.

All task-closure state transitions go through this module so the status
machine stays in one place; routes only parse input and serialize output.
"""

from __future__ import annotations

from datetime import UTC, datetime

from sqlalchemy import and_, exists, func, or_, select
from sqlalchemy.orm import Session

from app.core.errors import AppError
from app.models import Artifact, Task, TaskParticipant, TaskSubmission, User
from app.schemas.artifact import UserSummary
from app.schemas.auth import UserRole
from app.schemas.task import (
    ParticipantStatus,
    SubmissionStatus,
    SubmissionTaskSummary,
    TaskParticipantRead,
    TaskStatus,
    TaskSubmissionRead,
)

ACTIVE = ParticipantStatus.ACTIVE.value
LEFT = ParticipantStatus.LEFT.value

SUBMITTED = SubmissionStatus.SUBMITTED.value
REVISION_REQUIRED = SubmissionStatus.REVISION_REQUIRED.value
ACCEPTED = SubmissionStatus.ACCEPTED.value
REJECTED = SubmissionStatus.REJECTED.value

PENDING_SUBMISSION_STATUSES = (SUBMITTED, REVISION_REQUIRED)


def _utcnow() -> datetime:
    return datetime.now(UTC)


def _state_conflict(message: str) -> AppError:
    return AppError("TASK_STATE_CONFLICT", message, status_code=409)


def _is_terminal(task: Task) -> bool:
    return task.status in (TaskStatus.COMPLETED.value, TaskStatus.CLOSED.value)


def _is_admin(user: User) -> bool:
    return user.role == UserRole.SYSTEM_ADMIN.value


def get_submission(db: Session, submission_id: int) -> TaskSubmission:
    submission = db.get(TaskSubmission, submission_id)
    if submission is None:
        raise AppError("SUBMISSION_NOT_FOUND", "Task submission not found", status_code=404)
    return submission


def find_participant(db: Session, task_id: int, user_id: int) -> TaskParticipant | None:
    return db.scalar(
        select(TaskParticipant).where(
            TaskParticipant.task_id == task_id,
            TaskParticipant.user_id == user_id,
        )
    )


def _require_active_participant(db: Session, task: Task, user: User) -> TaskParticipant:
    participant = find_participant(db, task.id, user.id)
    if participant is None or participant.status != ACTIVE:
        raise AppError("FORBIDDEN", "Only an active participant can submit to this task", status_code=403)
    return participant


def _has_active_participant(db: Session, task_id: int) -> bool:
    return bool(
        db.scalar(
            select(exists().where(TaskParticipant.task_id == task_id, TaskParticipant.status == ACTIVE))
        )
    )


def _current_submission(db: Session, participant_id: int) -> TaskSubmission | None:
    return db.scalar(
        select(TaskSubmission).where(
            TaskSubmission.participant_id == participant_id,
            TaskSubmission.is_current.is_(True),
        )
    )


def _has_pending_submission(db: Session, task_id: int) -> bool:
    return bool(
        db.scalar(
            select(
                exists().where(
                    TaskSubmission.task_id == task_id,
                    TaskSubmission.is_current.is_(True),
                    TaskSubmission.status.in_(PENDING_SUBMISSION_STATUSES),
                )
            )
        )
    )


def _recompute_task_status(db: Session, task: Task) -> None:
    """Derive OPEN/IN_PROGRESS/REVIEWING from participants and pending rounds."""

    if _is_terminal(task):
        return
    # Sessions run with autoflush=False, so pending participant and submission
    # changes must be flushed before the existence checks below can see them.
    db.flush()
    if _has_pending_submission(db, task.id):
        target = TaskStatus.REVIEWING.value
    elif _has_active_participant(db, task.id):
        target = TaskStatus.IN_PROGRESS.value
    else:
        target = TaskStatus.OPEN.value
    if task.status != target:
        task.status = target
        task.updated_at = _utcnow()


def join_task(db: Session, task: Task, user: User) -> TaskParticipant:
    if _is_terminal(task):
        raise _state_conflict("A finished task cannot be joined")
    participant = find_participant(db, task.id, user.id)
    now = _utcnow()
    if participant is None:
        participant = TaskParticipant(
            task_id=task.id,
            user_id=user.id,
            status=ACTIVE,
            joined_at=now,
        )
        db.add(participant)
    elif participant.status == ACTIVE:
        raise AppError("TASK_ALREADY_PARTICIPATED", "You already participate in this task", status_code=409)
    else:
        participant.status = ACTIVE
        participant.joined_at = now
        participant.left_at = None
    _recompute_task_status(db, task)
    db.commit()
    db.refresh(participant)
    return participant


def leave_task(db: Session, task: Task, user: User) -> TaskParticipant:
    participant = find_participant(db, task.id, user.id)
    if participant is None:
        raise AppError("PARTICIPANT_NOT_FOUND", "You do not participate in this task", status_code=404)
    if _is_terminal(task):
        raise _state_conflict("A finished task cannot be left")
    has_submissions = bool(
        db.scalar(
            select(exists().where(TaskSubmission.participant_id == participant.id))
        )
    )
    if has_submissions:
        raise AppError(
            "TASK_SUBMISSION_EXISTS",
            "A participant with submissions cannot leave the task",
            status_code=409,
        )
    participant.status = LEFT
    participant.left_at = _utcnow()
    _recompute_task_status(db, task)
    db.commit()
    db.refresh(participant)
    return participant


def create_submission(
    db: Session,
    task: Task,
    user: User,
    artifact_id: int,
    note: str | None,
) -> TaskSubmission:
    if _is_terminal(task):
        raise _state_conflict("A finished task cannot receive submissions")
    participant = _require_active_participant(db, task, user)
    current = _current_submission(db, participant.id)
    if current is not None and current.status in (SUBMITTED, ACCEPTED):
        raise AppError(
            "SUBMISSION_ALREADY_PENDING",
            "The current submission is still pending or already accepted",
            status_code=409,
        )
    artifact = db.get(Artifact, artifact_id)
    if artifact is None:
        raise AppError("ARTIFACT_NOT_FOUND", "Artifact not found", status_code=404)
    if artifact.author_id != user.id or artifact.status != "PUBLISHED":
        raise AppError(
            "ARTIFACT_NOT_SUBMITTABLE",
            "Only your published artifacts can be submitted",
            status_code=422,
        )
    if current is not None:
        current.is_current = False
    last_round = int(
        db.scalar(
            select(func.max(TaskSubmission.round_no)).where(TaskSubmission.participant_id == participant.id)
        )
        or 0
    )
    submission = TaskSubmission(
        task_id=task.id,
        participant_id=participant.id,
        artifact_id=artifact.id,
        round_no=last_round + 1,
        note=note,
        status=SUBMITTED,
        is_current=True,
        submitted_at=_utcnow(),
    )
    db.add(submission)
    _recompute_task_status(db, task)
    db.commit()
    db.refresh(submission)
    return submission


def decide_submission(
    db: Session,
    submission: TaskSubmission,
    user: User,
    action: str,
    note: str | None,
) -> TaskSubmission:
    """Apply request-revision / accept / reject to a current submission."""

    task = db.get(Task, submission.task_id)
    if task is None:
        raise AppError("TASK_NOT_FOUND", "Task not found", status_code=404)
    if task.creator_id != user.id and not _is_admin(user):
        raise AppError("FORBIDDEN", "Only the task creator or an administrator can decide submissions", status_code=403)
    if not submission.is_current:
        raise AppError(
            "SUBMISSION_STATE_CONFLICT",
            "Only the current submission round can be decided",
            status_code=409,
        )
    now = _utcnow()
    if action == "request_revision":
        if submission.status != SUBMITTED:
            raise _state_submission_conflict()
        submission.status = REVISION_REQUIRED
        submission.revision_requested_at = now
    elif action == "accept":
        if submission.status not in (SUBMITTED, REVISION_REQUIRED):
            raise _state_submission_conflict()
        submission.status = ACCEPTED
        submission.decided_at = now
    elif action == "reject":
        if submission.status not in (SUBMITTED, REVISION_REQUIRED):
            raise _state_submission_conflict()
        submission.status = REJECTED
        submission.decided_at = now
    else:  # pragma: no cover - guarded by the route layer
        raise ValueError(f"unknown decision action: {action}")
    submission.decider_id = user.id
    submission.decision_note = note
    _recompute_task_status(db, task)
    db.commit()
    db.refresh(submission)
    return submission


def _state_submission_conflict() -> AppError:
    return AppError(
        "SUBMISSION_STATE_CONFLICT",
        "The submission status does not allow this action",
        status_code=409,
    )


def complete_task(db: Session, task: Task, user: User) -> Task:
    if task.creator_id != user.id:
        raise AppError("FORBIDDEN", "Only the task creator can complete this task", status_code=403)
    accepted_count = int(
        db.scalar(
            select(func.count())
            .select_from(TaskSubmission)
            .where(TaskSubmission.task_id == task.id, TaskSubmission.status == ACCEPTED)
        )
        or 0
    )
    if accepted_count == 0:
        raise AppError(
            "TASK_NO_ACCEPTED_RESULT",
            "The task cannot be completed without an accepted submission",
            status_code=409,
        )
    if task.status not in (TaskStatus.IN_PROGRESS.value, TaskStatus.REVIEWING.value):
        raise _state_conflict("Only an in-progress or reviewing task can be completed")
    now = _utcnow()
    task.status = TaskStatus.COMPLETED.value
    task.completed_at = now
    task.updated_at = now
    db.commit()
    return task


def close_task(db: Session, task: Task, user: User) -> Task:
    if task.creator_id != user.id and not _is_admin(user):
        raise AppError("FORBIDDEN", "Task permission is required", status_code=403)
    if _is_terminal(task):
        raise _state_conflict("A finished task cannot be closed")
    now = _utcnow()
    task.status = TaskStatus.CLOSED.value
    task.closed_at = now
    task.updated_at = now
    db.commit()
    return task


def reopen_task(db: Session, task: Task) -> Task:
    if task.status != TaskStatus.CLOSED.value:
        raise _state_conflict("Only a closed task can be reopened")
    task.status = (
        TaskStatus.IN_PROGRESS.value if _has_active_participant(db, task.id) else TaskStatus.OPEN.value
    )
    task.closed_at = None
    task.updated_at = _utcnow()
    db.commit()
    return task


def can_view_all_submissions(task: Task, user: User) -> bool:
    return task.creator_id == user.id or _is_admin(user)


def task_submission_visibility(task: Task, user: User):
    """Row condition limiting submissions visible to one viewer on one task.

    The creator and administrators see everything; other users see their own
    rounds plus accepted submissions once the task is completed.
    """

    own_participant_ids = select(TaskParticipant.id).where(
        TaskParticipant.task_id == task.id,
        TaskParticipant.user_id == user.id,
    )
    return or_(
        TaskSubmission.participant_id.in_(own_participant_ids),
        and_(
            task.status == TaskStatus.COMPLETED.value,
            TaskSubmission.status == ACCEPTED,
        ),
    )


def participant_read(participant: TaskParticipant) -> TaskParticipantRead:
    return TaskParticipantRead(
        id=participant.id,
        task_id=participant.task_id,
        user=UserSummary.model_validate(participant.user),
        status=participant.status,
        joined_at=participant.joined_at,
        left_at=participant.left_at,
    )


def submission_read(submission: TaskSubmission, include_task: bool = False) -> TaskSubmissionRead:
    task_summary: SubmissionTaskSummary | None = None
    if include_task:
        task = submission.task
        task_summary = SubmissionTaskSummary(id=task.id, title=task.title, status=task.status)
    decider = submission.decider
    return TaskSubmissionRead(
        id=submission.id,
        task_id=submission.task_id,
        participant_id=submission.participant_id,
        participant=UserSummary.model_validate(submission.participant.user),
        artifact={
            "id": submission.artifact.id,
            "title": submission.artifact.title,
            "status": submission.artifact.status,
        },
        round_no=submission.round_no,
        note=submission.note,
        status=submission.status,
        is_current=submission.is_current,
        submitted_at=submission.submitted_at,
        revision_requested_at=submission.revision_requested_at,
        decided_at=submission.decided_at,
        decider=UserSummary.model_validate(decider) if decider is not None else None,
        decision_note=submission.decision_note,
        task=task_summary,
    )
