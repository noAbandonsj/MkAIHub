"""Competition browsing, participation, and administrator maintenance."""

from __future__ import annotations

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, Query, status
from sqlalchemy import func, or_, select
from sqlalchemy.orm import Session, joinedload

from app.api.v1.deps import AuthContext, get_current_auth, require_admin_csrf, require_csrf
from app.core.errors import AppError
from app.core.logging import log_admin_action
from app.db.session import get_db
from app.models import Competition, CompetitionRegistration
from app.schemas.auth import UserRole
from app.schemas.competition import (
    CompetitionCreate,
    CompetitionListResponse,
    CompetitionRead,
    CompetitionRegistrationListResponse,
    CompetitionResultsRead,
    CompetitionTaskCreate,
    CompetitionTaskListResponse,
    CompetitionTaskUpdate,
    CompetitionUpdate,
    PublishResultsRequest,
)
from app.schemas.task import TaskRead
from app.services import competition_closure
from app.services.competition_closure import registration_read
from app.services.competitions import competition_list_item, competition_read, get_competition
from app.services.tasks import task_read


router = APIRouter(prefix="/competitions", tags=["competitions"])
admin_router = APIRouter(prefix="/admin/competitions", tags=["admin"])


def _is_admin(auth: AuthContext) -> bool:
    return auth.user.role == UserRole.SYSTEM_ADMIN.value


@router.get("", response_model=CompetitionListResponse, summary="List competitions")
def list_competitions(
    page: int = Query(1, ge=1),
    page_size: int = Query(12, ge=1, le=100),
    q: str | None = Query(None, max_length=100),
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> CompetitionListResponse:
    conditions = []
    if not _is_admin(auth):
        # DRAFT competitions are administrator-only until explicitly published.
        conditions.append(Competition.status != "DRAFT")
    search = q.strip() if q else ""
    if search:
        pattern = f"%{search}%"
        conditions.append(or_(Competition.title.ilike(pattern), Competition.summary.ilike(pattern)))

    count_statement = select(func.count()).select_from(Competition).where(*conditions)
    statement = (
        select(Competition)
        .options(joinedload(Competition.creator))
        .where(*conditions)
        .order_by(Competition.start_at.desc(), Competition.id.desc())
        .offset((page - 1) * page_size)
        .limit(page_size)
    )
    total = int(db.scalar(count_statement) or 0)
    competitions = list(db.scalars(statement).all())
    return CompetitionListResponse(
        items=[competition_list_item(item) for item in competitions],
        page=page,
        page_size=page_size,
        total=total,
    )


@router.get("/{competition_id}", response_model=CompetitionRead, summary="Get competition")
def get_competition_endpoint(
    competition_id: int,
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> CompetitionRead:
    competition = competition_closure.get_competition_for_viewer(db, competition_id, auth.user)
    return competition_read(db, competition, viewer=auth.user)


@router.post(
    "/{competition_id}/registrations",
    status_code=status.HTTP_201_CREATED,
    summary="Register for competition",
)
def register_for_competition(
    competition_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
):
    competition = competition_closure.get_competition_for_viewer(db, competition_id, auth.user)
    registration = competition_closure.register_competition(db, competition, auth.user)
    return registration_read(registration)


@router.delete("/{competition_id}/registrations/me", summary="Cancel my registration")
def cancel_my_registration(
    competition_id: int,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
):
    competition = competition_closure.get_competition_for_viewer(db, competition_id, auth.user)
    registration = competition_closure.cancel_registration(db, competition, auth.user)
    return registration_read(registration)


@router.get("/{competition_id}/tasks", response_model=CompetitionTaskListResponse, summary="List competition tasks")
def list_competition_tasks(
    competition_id: int,
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> CompetitionTaskListResponse:
    competition = competition_closure.get_competition_for_viewer(db, competition_id, auth.user)
    return CompetitionTaskListResponse(
        items=competition_closure.competition_tasks_read(db, competition, auth.user)
    )


@router.get("/{competition_id}/results", response_model=CompetitionResultsRead, summary="Get competition leaderboard")
def get_competition_results(
    competition_id: int,
    auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
):
    competition = competition_closure.get_competition_for_viewer(db, competition_id, auth.user)
    if competition.status not in ("RESULT_PUBLISHED", "ARCHIVED"):
        raise AppError(
            "COMPETITION_RESULTS_NOT_PUBLISHED",
            "Competition results have not been published",
            status_code=404,
        )
    return competition_closure.results_read(db, competition)


def _validate_window(start_at: datetime, end_at: datetime) -> None:
    start_utc = start_at if start_at.tzinfo else start_at.replace(tzinfo=UTC)
    end_utc = end_at if end_at.tzinfo else end_at.replace(tzinfo=UTC)
    if start_utc >= end_utc:
        raise AppError(
            "COMPETITION_TIME_CONFLICT",
            "start_at must be earlier than end_at",
            status_code=422,
        )


@admin_router.post("", response_model=CompetitionRead, status_code=status.HTTP_201_CREATED, summary="Create competition")
def create_competition(
    payload: CompetitionCreate,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> CompetitionRead:
    competition = Competition(
        title=payload.title,
        summary=payload.summary,
        rules_markdown=payload.rules_markdown,
        start_at=payload.start_at,
        end_at=payload.end_at,
        status="DRAFT",
        created_by=auth.user.id,
    )
    db.add(competition)
    db.commit()
    log_admin_action(
        "competition.create",
        actor_id=auth.user.id,
        target_type="competition",
        target_id=competition.id,
        title=competition.title,
    )
    return competition_read(db, get_competition(db, competition.id), viewer=auth.user)


@admin_router.patch("/{competition_id}", response_model=CompetitionRead, summary="Update competition")
def update_competition(
    competition_id: int,
    payload: CompetitionUpdate,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> CompetitionRead:
    competition = get_competition(db, competition_id)
    if competition.status in ("RESULT_PUBLISHED", "ARCHIVED"):
        raise AppError(
            "COMPETITION_STATE_CONFLICT",
            "A result-published or archived competition cannot be edited",
            status_code=409,
        )
    changes = payload.model_dump(exclude_unset=True)
    if any(value is None for value in changes.values()):
        raise AppError("COMPETITION_FIELD_REQUIRED", "Competition fields cannot be null", status_code=422)
    if competition.status == "PUBLISHED" and "start_at" in changes:
        raise AppError(
            "COMPETITION_FIELD_LOCKED",
            "start_at cannot be changed after the competition is published",
            status_code=422,
        )
    for field in ("title", "summary", "rules_markdown", "start_at", "end_at"):
        if field in changes:
            setattr(competition, field, changes[field])
    _validate_window(competition.start_at, competition.end_at)
    if changes:
        competition.updated_at = datetime.now(UTC)
        db.commit()
    log_admin_action(
        "competition.update",
        actor_id=auth.user.id,
        target_type="competition",
        target_id=competition.id,
        fields=sorted(changes),
    )
    return competition_read(db, get_competition(db, competition_id), viewer=auth.user)


@admin_router.delete("/{competition_id}", status_code=status.HTTP_204_NO_CONTENT, summary="Delete competition")
def delete_competition(
    competition_id: int,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> None:
    competition = get_competition(db, competition_id)
    if competition_closure.competition_has_references(db, competition.id):
        raise AppError(
            "COMPETITION_HAS_REFERENCES",
            "A competition with tasks, registrations, or results cannot be deleted",
            status_code=409,
        )
    competition_title = competition.title
    db.delete(competition)
    db.commit()
    log_admin_action(
        "competition.delete",
        actor_id=auth.user.id,
        target_type="competition",
        target_id=competition_id,
        title=competition_title,
    )


@admin_router.get(
    "/{competition_id}/registrations",
    response_model=CompetitionRegistrationListResponse,
    summary="List competition registrations",
)
def list_competition_registrations(
    competition_id: int,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> CompetitionRegistrationListResponse:
    get_competition(db, competition_id)
    registrations = list(
        db.scalars(
            select(CompetitionRegistration)
            .options(joinedload(CompetitionRegistration.user))
            .where(CompetitionRegistration.competition_id == competition_id)
            .order_by(CompetitionRegistration.id.asc())
        ).all()
    )
    return CompetitionRegistrationListResponse(items=[registration_read(item) for item in registrations])


@admin_router.post(
    "/{competition_id}/tasks",
    response_model=TaskRead,
    status_code=status.HTTP_201_CREATED,
    summary="Create competition task",
)
def create_competition_task(
    competition_id: int,
    payload: CompetitionTaskCreate,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
):
    competition = get_competition(db, competition_id)
    task = competition_closure.create_competition_task(db, competition, auth.user, payload)
    log_admin_action(
        "competition.task_create",
        actor_id=auth.user.id,
        target_type="task",
        target_id=task.id,
        competition_id=competition.id,
        title=task.title,
    )
    return task_read(db, task, viewer=auth.user)


@admin_router.patch("/{competition_id}/tasks/{task_id}", response_model=TaskRead, summary="Update competition task")
def update_competition_task_endpoint(
    competition_id: int,
    task_id: int,
    payload: CompetitionTaskUpdate,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
):
    competition = get_competition(db, competition_id)
    task = competition_closure.get_competition_task(db, competition, task_id)
    task = competition_closure.update_competition_task(db, competition, task, payload)
    log_admin_action(
        "competition.task_update",
        actor_id=auth.user.id,
        target_type="task",
        target_id=task.id,
        competition_id=competition.id,
        fields=sorted(payload.model_dump(exclude_unset=True)),
    )
    return task_read(db, task, viewer=auth.user)


@admin_router.delete(
    "/{competition_id}/tasks/{task_id}",
    status_code=status.HTTP_204_NO_CONTENT,
    summary="Delete competition task",
)
def delete_competition_task_endpoint(
    competition_id: int,
    task_id: int,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> None:
    competition = get_competition(db, competition_id)
    task = competition_closure.get_competition_task(db, competition, task_id)
    competition_closure.delete_competition_task(db, competition, task)
    log_admin_action(
        "competition.task_delete",
        actor_id=auth.user.id,
        target_type="task",
        target_id=task_id,
        competition_id=competition.id,
        title=task.title,
    )


@admin_router.post("/{competition_id}/publish", response_model=CompetitionRead, summary="Publish competition")
def publish_competition_endpoint(
    competition_id: int,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> CompetitionRead:
    competition = get_competition(db, competition_id)
    competition = competition_closure.publish_competition(db, competition)
    log_admin_action(
        "competition.publish",
        actor_id=auth.user.id,
        target_type="competition",
        target_id=competition.id,
        title=competition.title,
    )
    return competition_read(db, get_competition(db, competition_id), viewer=auth.user)


@admin_router.post("/{competition_id}/publish-results", response_model=CompetitionRead, summary="Publish competition results")
def publish_competition_results(
    competition_id: int,
    payload: PublishResultsRequest | None = None,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> CompetitionRead:
    competition = get_competition(db, competition_id)
    awards = (
        [award.model_dump() for award in payload.awards] if payload is not None and payload.awards else None
    )
    competition, republished, rows = competition_closure.publish_results(db, competition, auth.user, awards)
    log_admin_action(
        "competition.republish_results" if republished else "competition.publish_results",
        actor_id=auth.user.id,
        target_type="competition",
        target_id=competition.id,
        result_count=len(rows),
    )
    return competition_read(db, get_competition(db, competition_id), viewer=auth.user)


@admin_router.post("/{competition_id}/archive", response_model=CompetitionRead, summary="Archive competition")
def archive_competition_endpoint(
    competition_id: int,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> CompetitionRead:
    competition = get_competition(db, competition_id)
    competition = competition_closure.archive_competition(db, competition)
    log_admin_action(
        "competition.archive",
        actor_id=auth.user.id,
        target_type="competition",
        target_id=competition.id,
        title=competition.title,
    )
    return competition_read(db, get_competition(db, competition_id), viewer=auth.user)
