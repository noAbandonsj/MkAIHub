"""Competition query, computed status, and response helpers."""

from __future__ import annotations

from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.orm import Session, joinedload

from app.core.errors import AppError
from app.models import Competition
from app.models.artifact import utcnow
from app.schemas.artifact import UserSummary
from app.schemas.competition import CompetitionListItem, CompetitionRead, CompetitionStatus


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
        start_at=competition.start_at,
        end_at=competition.end_at,
        created_at=competition.created_at,
        updated_at=competition.updated_at,
    )


def competition_read(competition: Competition) -> CompetitionRead:
    return CompetitionRead(
        **competition_list_item(competition).model_dump(),
        rules_markdown=competition.rules_markdown,
    )
