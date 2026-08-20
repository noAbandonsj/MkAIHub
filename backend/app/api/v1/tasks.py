"""Task CRUD and state actions."""

from __future__ import annotations

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, Query, status
from sqlalchemy import func, or_, select
from sqlalchemy.orm import Session, joinedload

from app.api.v1.deps import AuthContext, get_current_auth, require_csrf
from app.core.errors import AppError
from app.db.session import get_db
from app.models import Task
from app.schemas.task import (
    TaskCreate,
    TaskListResponse,
    TaskRead,
    TaskStatus,
    TaskUpdate,
)
from app.services.tasks import (
    get_task,
    require_task_creator,
    require_task_creator_or_admin,
    task_list_item,
    task_read,
)


router = APIRouter(prefix="/tasks", tags=["tasks"])


def _state_conflict(message: str) -> AppError:
    return AppError("TASK_STATE_CONFLICT", message, status_code=409)


@router.get("", response_model=TaskListResponse, summary="List tasks")
def list_tasks(
    page: int = Query(1, ge=1),
    page_size: int = Query(12, ge=1, le=100),
    q: str | None = Query(None, max_length=100),
    mine: bool = False,
    status_filter: TaskStatus | None = Query(None, alias="status"),
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> TaskListResponse:
    conditions = []
    if mine:
        conditions.append(Task.creator_id == auth.user.id)
    if status_filter is not None:
        conditions.append(Task.status == status_filter.value)
    search = q.strip() if q else ""
    if search:
        pattern = f"%{search}%"
        conditions.append(or_(Task.title.ilike(pattern), Task.description.ilike(pattern)))

    count_statement = select(func.count()).select_from(Task).where(*conditions)
    statement = (
        select(Task)
        .options(joinedload(Task.creator))
        .where(*conditions)
        .order_by(Task.updated_at.desc(), Task.id.desc())
        .offset((page - 1) * page_size)
        .limit(page_size)
    )
    total = int(db.scalar(count_statement) or 0)
    tasks = list(db.scalars(statement).all())
    return TaskListResponse(
        items=[task_list_item(item) for item in tasks],
        page=page,
        page_size=page_size,
        total=total,
    )


@router.post("", response_model=TaskRead, status_code=status.HTTP_201_CREATED, summary="Create task")
def create_task(
    payload: TaskCreate,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> TaskRead:
    task = Task(
        title=payload.title,
        description=payload.description,
        deadline_at=payload.deadline_at,
        creator_id=auth.user.id,
        status=TaskStatus.OPEN.value,
    )
    db.add(task)
    db.commit()
    return task_read(get_task(db, task.id))


@router.get("/{task_id}", response_model=TaskRead, summary="Get task")
def get_task_endpoint(
    task_id: int,
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> TaskRead:
    return task_read(get_task(db, task_id))


@router.patch("/{task_id}", response_model=TaskRead, summary="Update task")
def update_task(
    task_id: int,
    payload: TaskUpdate,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> TaskRead:
    task = get_task(db, task_id)
    require_task_creator(task, auth.user)
    if task.status != TaskStatus.OPEN.value:
        raise _state_conflict("Only an open task can be edited")

    changes = payload.model_dump(exclude_unset=True)
    if any(value is None for field, value in changes.items() if field in ("title", "description")):
        raise AppError("TASK_FIELD_REQUIRED", "Task fields cannot be null", status_code=422)
    for field in ("title", "description"):
        if field in changes:
            setattr(task, field, changes[field])
    if "deadline_at" in changes:
        task.deadline_at = changes["deadline_at"]
    if changes:
        task.updated_at = datetime.now(UTC)
        db.commit()
    return task_read(get_task(db, task.id))


@router.post("/{task_id}/complete", response_model=TaskRead, summary="Complete task")
def complete_task(
    task_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> TaskRead:
    task = get_task(db, task_id)
    require_task_creator(task, auth.user)
    if task.status != TaskStatus.OPEN.value:
        raise _state_conflict("Only an open task can be completed")
    now = datetime.now(UTC)
    task.status = TaskStatus.COMPLETED.value
    task.completed_at = now
    task.updated_at = now
    db.commit()
    return task_read(get_task(db, task.id))


@router.post("/{task_id}/close", response_model=TaskRead, summary="Close task")
def close_task(
    task_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> TaskRead:
    task = get_task(db, task_id)
    require_task_creator_or_admin(task, auth.user)
    if task.status != TaskStatus.OPEN.value:
        raise _state_conflict("Only an open task can be closed")
    now = datetime.now(UTC)
    task.status = TaskStatus.CLOSED.value
    task.closed_at = now
    task.updated_at = now
    db.commit()
    return task_read(get_task(db, task.id))
