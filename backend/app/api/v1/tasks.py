"""Task CRUD, closure actions, participation, and submissions."""

from __future__ import annotations

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, Query, status
from sqlalchemy import exists, func, or_, select
from sqlalchemy.orm import Session, joinedload

from app.api.v1.deps import AuthContext, get_current_auth, require_csrf
from app.core.errors import AppError
from app.db.session import get_db
from app.models import Task, TaskParticipant, TaskSubmission
from app.schemas.task import (
    TaskCreate,
    TaskListResponse,
    TaskParticipantListResponse,
    TaskParticipantRead,
    TaskRead,
    TaskStatus,
    TaskSubmissionCreate,
    TaskSubmissionListResponse,
    TaskSubmissionRead,
    TaskUpdate,
)
from app.services import task_closure
from app.services.tasks import get_task, require_task_creator, task_list_item, task_read


router = APIRouter(prefix="/tasks", tags=["tasks"])

TERMINAL_STATUSES = (TaskStatus.COMPLETED.value, TaskStatus.CLOSED.value)
PENDING_SUBMISSION_STATUSES = (task_closure.SUBMITTED, task_closure.REVISION_REQUIRED)


def _state_conflict(message: str) -> AppError:
    return AppError("TASK_STATE_CONFLICT", message, status_code=409)


@router.get("", response_model=TaskListResponse, summary="List tasks")
def list_tasks(
    page: int = Query(1, ge=1),
    page_size: int = Query(12, ge=1, le=100),
    q: str | None = Query(None, max_length=100),
    mine: bool = False,
    participated: bool = False,
    pending_review: bool = False,
    status_filter: TaskStatus | None = Query(None, alias="status"),
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> TaskListResponse:
    conditions = []
    if mine:
        conditions.append(Task.creator_id == auth.user.id)
    if participated:
        participated_task_ids = select(TaskParticipant.task_id).where(
            TaskParticipant.user_id == auth.user.id,
            TaskParticipant.status == "ACTIVE",
        )
        conditions.append(Task.id.in_(participated_task_ids))
    if pending_review:
        conditions.append(Task.creator_id == auth.user.id)
        conditions.append(
            exists().where(
                TaskSubmission.task_id == Task.id,
                TaskSubmission.is_current.is_(True),
                TaskSubmission.status.in_(PENDING_SUBMISSION_STATUSES),
            )
        )
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
    return task_read(db, task, viewer=auth.user)


@router.get("/{task_id}", response_model=TaskRead, summary="Get task")
def get_task_endpoint(
    task_id: int,
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> TaskRead:
    return task_read(db, get_task(db, task_id), viewer=auth.user)


@router.patch("/{task_id}", response_model=TaskRead, summary="Update task")
def update_task(
    task_id: int,
    payload: TaskUpdate,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> TaskRead:
    task = get_task(db, task_id)
    require_task_creator(task, auth.user)
    if task.status in TERMINAL_STATUSES:
        raise _state_conflict("A finished task cannot be edited")

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
    return task_read(db, task, viewer=auth.user)


@router.post("/{task_id}/complete", response_model=TaskRead, summary="Complete task")
def complete_task(
    task_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> TaskRead:
    task = get_task(db, task_id)
    task_closure.complete_task(db, task, auth.user)
    return task_read(db, task, viewer=auth.user)


@router.post("/{task_id}/close", response_model=TaskRead, summary="Close task")
def close_task(
    task_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> TaskRead:
    task = get_task(db, task_id)
    task_closure.close_task(db, task, auth.user)
    return task_read(db, task, viewer=auth.user)


@router.post("/{task_id}/participants", response_model=TaskParticipantRead, status_code=status.HTTP_201_CREATED, summary="Join task")
def join_task(
    task_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
):
    task = get_task(db, task_id)
    participant = task_closure.join_task(db, task, auth.user)
    return task_closure.participant_read(participant)


@router.delete("/{task_id}/participants/me", response_model=TaskParticipantRead, summary="Leave task")
def leave_task(
    task_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
):
    task = get_task(db, task_id)
    participant = task_closure.leave_task(db, task, auth.user)
    return task_closure.participant_read(participant)


@router.get("/{task_id}/participants", response_model=TaskParticipantListResponse, summary="List task participants")
def list_participants(
    task_id: int,
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> TaskParticipantListResponse:
    get_task(db, task_id)
    participants = list(
        db.scalars(
            select(TaskParticipant)
            .options(joinedload(TaskParticipant.user))
            .where(TaskParticipant.task_id == task_id)
            .order_by(TaskParticipant.joined_at.asc(), TaskParticipant.id.asc())
        ).all()
    )
    return TaskParticipantListResponse(items=[task_closure.participant_read(item) for item in participants])


@router.get("/{task_id}/submissions", response_model=TaskSubmissionListResponse, summary="List task submissions")
def list_task_submissions(
    task_id: int,
    page: int = Query(1, ge=1),
    page_size: int = Query(20, ge=1, le=100),
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> TaskSubmissionListResponse:
    task = get_task(db, task_id)
    conditions = [TaskSubmission.task_id == task_id]
    if not task_closure.can_view_all_submissions(task, auth.user):
        conditions.append(task_closure.task_submission_visibility(task, auth.user))
    count_statement = select(func.count()).select_from(TaskSubmission).where(*conditions)
    statement = (
        select(TaskSubmission)
        .where(*conditions)
        .order_by(TaskSubmission.submitted_at.desc(), TaskSubmission.id.desc())
        .offset((page - 1) * page_size)
        .limit(page_size)
    )
    total = int(db.scalar(count_statement) or 0)
    submissions = list(db.scalars(statement).all())
    return TaskSubmissionListResponse(
        items=[task_closure.submission_read(item) for item in submissions],
        page=page,
        page_size=page_size,
        total=total,
    )


@router.post("/{task_id}/submissions", response_model=TaskSubmissionRead, status_code=status.HTTP_201_CREATED, summary="Submit artifact to task")
def submit_to_task(
    task_id: int,
    payload: TaskSubmissionCreate,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
):
    task = get_task(db, task_id)
    submission = task_closure.create_submission(db, task, auth.user, payload.artifact_id, payload.note)
    return task_closure.submission_read(submission)
