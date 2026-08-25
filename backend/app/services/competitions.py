"""Competition query, computed status, and response helpers."""

from __future__ import annotations

from datetime import UTC, datetime

from sqlalchemy import exists, func, select
from sqlalchemy.orm import Session, joinedload

from app.core.errors import AppError
from app.models import Competition, CompetitionRegistration, CompetitionResult, Task, User
from app.models.artifact import utcnow
from app.schemas.artifact import UserSummary
from app.schemas.competition import (
    CompetitionListItem,
    CompetitionRead,
    CompetitionRegistrationSummary,
    CompetitionStatus,
)


def _as_utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=UTC)
    return value.astimezone(UTC)


def competition_status(competition: Competition, *, now: datetime | None = None) -> CompetitionStatus:
    """Compute UPCOMING/ONGOING/ENDED from the server clock, never stored."""

    current = _as_utc(now) if now is not None else utcnow()
    if current < _as_utc(competition.start_at):
        return CompetitionStatus.UPCOMING
    if current < _as_utc(competition.end_at):
        return CompetitionStatus.ONGOING
    return CompetitionStatus.ENDED


def get_competition(db: Session, competition_id: int) -> Competition:
    competition = db.scalar(
        select(Competition).options(joinedload(Competition.creator)).where(Competition.id == competition_id)
    )
    if competition is None:
        raise AppError("COMPETITION_NOT_FOUND", "Competition not found", status_code=404)
    return competition


def competition_list_item(competition: Competition) -> CompetitionListItem:
    return CompetitionListItem(
        id=competition.id,
        title=competition.title,
        summary=competition.summary,
        creator=UserSummary.model_validate(competition.creator),
        status=competition_status(competition),
        lifecycle_status=competition.status,
        start_at=competition.start_at,
        end_at=competition.end_at,
        created_at=competition.created_at,
        updated_at=competition.updated_at,
    )


def competition_read(db: Session, competition: Competition, viewer: User | None = None) -> CompetitionRead:
    task_count = int(
        db.scalar(select(func.count()).select_from(Task).where(Task.competition_id == competition.id)) or 0
    )
    registration_count = int(
        db.scalar(
            select(func.count())
            .select_from(CompetitionRegistration)
            .where(
                CompetitionRegistration.competition_id == competition.id,
                CompetitionRegistration.status == "REGISTERED",
            )
        )
        or 0
    )
    my_registration: CompetitionRegistrationSummary | None = None
    if viewer is not None:
        registration = db.scalar(
            select(CompetitionRegistration).where(
                CompetitionRegistration.competition_id == competition.id,
                CompetitionRegistration.user_id == viewer.id,
            )
        )
        if registration is not None:
            my_registration = CompetitionRegistrationSummary(
                id=registration.id,
                status=registration.status,
                registered_at=registration.registered_at,
                cancelled_at=registration.cancelled_at,
            )
    results_published = bool(
        db.scalar(select(exists().where(CompetitionResult.competition_id == competition.id)))
    )
    return CompetitionRead(
        **competition_list_item(competition).model_dump(),
        rules_markdown=competition.rules_markdown,
        task_count=task_count,
        registration_count=registration_count,
        my_registration=my_registration,
        results_published=results_published,
    )
