"""Read-only aggregates for the batch-10 personal workbench."""

from __future__ import annotations

from sqlalchemy import distinct, func, select
from sqlalchemy.orm import Session

from app.models import (
    Competition,
    CompetitionResult,
    CompetitionReview,
    Task,
    TaskParticipant,
    TaskSubmission,
    User,
)
from app.schemas.auth import UserRole
from app.schemas.workbench import ClosureStatistics, WorkbenchCounts, WorkbenchResponse
from app.services import task_closure


def _count(db: Session, statement) -> int:
    return int(db.scalar(statement) or 0)


def workbench_response(db: Session, user: User) -> WorkbenchResponse:
    active_participation = (
        TaskParticipant.user_id == user.id,
        TaskParticipant.status == task_closure.ACTIVE,
    )
    participated_tasks = _count(
        db,
        select(func.count(distinct(TaskParticipant.task_id))).where(*active_participation),
    )
    competition_tasks = _count(
        db,
        select(func.count(distinct(TaskParticipant.task_id)))
        .select_from(TaskParticipant)
        .join(Task, Task.id == TaskParticipant.task_id)
        .where(*active_participation, Task.competition_id.is_not(None)),
    )
    pending_task_reviews = _count(
        db,
        select(func.count())
        .select_from(TaskSubmission)
        .join(Task, Task.id == TaskSubmission.task_id)
        .where(
            Task.creator_id == user.id,
            Task.competition_id.is_(None),
            TaskSubmission.is_current.is_(True),
            TaskSubmission.status.in_(task_closure.PENDING_SUBMISSION_STATUSES),
        ),
    )

    pending_competition_reviews = 0
    if user.role == UserRole.SYSTEM_ADMIN.value:
        pending_competition_reviews = _count(
            db,
            select(func.count())
            .select_from(TaskSubmission)
            .join(Task, Task.id == TaskSubmission.task_id)
            .join(Competition, Competition.id == Task.competition_id)
            .outerjoin(
                CompetitionReview,
                CompetitionReview.task_submission_id == TaskSubmission.id,
            )
            .where(
                TaskSubmission.is_current.is_(True),
                Competition.status == "PUBLISHED",
                CompetitionReview.id.is_(None),
            ),
        )

    statistics = ClosureStatistics(
        participations=_count(
            db,
            select(func.count()).select_from(TaskParticipant).where(
                TaskParticipant.status == task_closure.ACTIVE
            ),
        ),
        submissions=_count(db, select(func.count()).select_from(TaskSubmission)),
        accepted_submissions=_count(
            db,
            select(func.count()).select_from(TaskSubmission).where(
                TaskSubmission.status == task_closure.ACCEPTED
            ),
        ),
        competition_task_completions=_count(
            db,
            select(func.count())
            .select_from(TaskSubmission)
            .join(Task, Task.id == TaskSubmission.task_id)
            .where(
                Task.competition_id.is_not(None),
                TaskSubmission.is_current.is_(True),
            ),
        ),
        published_results=_count(
            db,
            select(func.count(distinct(CompetitionResult.competition_id))).select_from(
                CompetitionResult
            ),
        ),
    )
    return WorkbenchResponse(
        counts=WorkbenchCounts(
            participated_tasks=participated_tasks,
            competition_tasks=competition_tasks,
            pending_task_reviews=pending_task_reviews,
            pending_competition_reviews=pending_competition_reviews,
        ),
        statistics=statistics,
    )
