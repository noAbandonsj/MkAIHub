"""Task query, authorization, and response helpers."""

from __future__ import annotations

from sqlalchemy import select
from sqlalchemy.orm import Session, joinedload

from app.core.errors import AppError
from app.models import Task, User
from app.schemas.auth import UserRole
from app.schemas.artifact import UserSummary
from app.schemas.task import TaskListItem, TaskRead


def get_task(db: Session, task_id: int) -> Task:
    task = db.scalar(
        select(Task).options(joinedload(Task.creator)).where(Task.id == task_id)
    )
    if task is None:
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
        deadline_at=task.deadline_at,
        completed_at=task.completed_at,
        closed_at=task.closed_at,
        created_at=task.created_at,
        updated_at=task.updated_at,
    )


def task_read(task: Task) -> TaskRead:
    return TaskRead(
        **task_list_item(task).model_dump(),
        description=task.description,
    )
