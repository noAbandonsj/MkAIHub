"""Competition browsing and administrator maintenance."""

from __future__ import annotations

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, Query, status
from sqlalchemy import func, or_, select
from sqlalchemy.orm import Session, joinedload

from app.api.v1.deps import AuthContext, get_current_auth, require_admin_csrf
from app.core.errors import AppError
from app.core.logging import log_admin_action
from app.db.session import get_db
from app.models import Competition
from app.schemas.competition import (
    CompetitionCreate,
    CompetitionListResponse,
    CompetitionRead,
    CompetitionUpdate,
)
from app.services.competitions import competition_list_item, competition_read, get_competition


router = APIRouter(prefix="/competitions", tags=["competitions"])
admin_router = APIRouter(prefix="/admin/competitions", tags=["admin"])


@router.get("", response_model=CompetitionListResponse, summary="List competitions")
def list_competitions(
    page: int = Query(1, ge=1),
    page_size: int = Query(12, ge=1, le=100),
    q: str | None = Query(None, max_length=100),
    _auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> CompetitionListResponse:
    conditions = []
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
    _auth: AuthContext = Depends(get_current_auth),
    db: Session = Depends(get_db),
) -> CompetitionRead:
    return competition_read(get_competition(db, competition_id))


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
    return competition_read(get_competition(db, competition.id))


@admin_router.patch("/{competition_id}", response_model=CompetitionRead, summary="Update competition")
def update_competition(
    competition_id: int,
    payload: CompetitionUpdate,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> CompetitionRead:
    competition = get_competition(db, competition_id)
    changes = payload.model_dump(exclude_unset=True)
    if any(value is None for value in changes.values()):
        raise AppError("COMPETITION_FIELD_REQUIRED", "Competition fields cannot be null", status_code=422)
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
    return competition_read(get_competition(db, competition_id))


@admin_router.delete("/{competition_id}", status_code=status.HTTP_204_NO_CONTENT, summary="Delete competition")
def delete_competition(
    competition_id: int,
    auth: AuthContext = Depends(require_admin_csrf),
    db: Session = Depends(get_db),
) -> None:
    competition = get_competition(db, competition_id)
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
