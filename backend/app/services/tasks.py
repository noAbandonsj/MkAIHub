"""Task query, authorization, and response helpers."""

from __future__ import annotations

from sqlalchemy import func, select
from sqlalchemy.orm import Session, joinedload

from app.core.errors import AppError
from app.models import Task, TaskParticipant, TaskSubmission, User
from app.schemas.auth import UserRole
from app.schemas.artifact import UserSummary
from app.schemas.task import TaskListItem, TaskParticipantRead, TaskRead


def _is_admin(user: User) -> bool:
    return user.role == UserRole.SYSTEM_ADMIN.value


def get_task(db: Session, task_id: int, viewer: User | None = None) -> Task:
    """Load a task; tasks under DRAFT competitions stay invisible to employees."""

    task = db.scalar(
        select(Task)
        .options(joinedload(Task.creator), joinedload(Task.competition))
        .where(Task.id == task_id)
    )
    if task is None:
        raise AppError("TASK_NOT_FOUND", "Task not found", status_code=404)
    if (
        viewer is not None
        and task.competition_id is not None
        and not _is_admin(viewer)
        and (task.competition is None or task.competition.status == "DRAFT")
    ):
        raise AppError("TASK_NOT_FOUND", "Task not found", status_code=404)
    return task


def require_task_creator(task: Task, user: User) -> None:
    if task.creator_id != user.id:
        raise AppError("FORBIDDEN", "Only the task creator can edit this task", status_code=403)


def require_task_creator_or_admin(task: Task, user: User) -> None:
    if task.creator_id != user.id and user.role != UserRole.SYSTEM_ADMIN.value:
        raise AppError("FORBIDDEN", "Task permission is required", status_code=403)


def task_list_item(task: Task) -> TaskListItem:
    return TaskListItem(
        id=task.id,
        title=task.title,
        creator=UserSummary.model_validate(task.creator),
        status=task.status,
        competition_id=task.competition_id,
        competition_title=task.competition.title if task.competition is not None else None,
        deadline_at=task.deadline_at,
        completed_at=task.completed_at,
        closed_at=task.closed_at,
        created_at=task.created_at,
        updated_at=task.updated_at,
    )


def task_read(db: Session, task: Task, viewer: User | None = None) -> TaskRead:
    participant_count = int(
        db.scalar(
            select(func.count())
            .select_from(TaskParticipant)
            .where(TaskParticipant.task_id == task.id, TaskParticipant.status == "ACTIVE")
        )
        or 0
    )
    submission_count = int(
        db.scalar(
            select(func.count())
            .select_from(TaskSubmission)
            .where(TaskSubmission.task_id == task.id)
        )
        or 0
    )
    my_participation: TaskParticipantRead | None = None
    if viewer is not None:
        participant = db.scalar(
            select(TaskParticipant).where(
                TaskParticipant.task_id == task.id,
                TaskParticipant.user_id == viewer.id,
            )
        )
        if participant is not None:
            my_participation = TaskParticipantRead(
                id=participant.id,
                task_id=participant.task_id,
                user=UserSummary.model_validate(participant.user),
                status=participant.status,
                joined_at=participant.joined_at,
                left_at=participant.left_at,
            )
    return TaskRead(
        **task_list_item(task).model_dump(),
        description=task.description,
        my_participation=my_participation,
        participant_count=participant_count,
        submission_count=submission_count,
    )
