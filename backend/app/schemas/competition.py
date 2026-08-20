"""API schemas for competitions."""

from __future__ import annotations

from datetime import UTC, datetime
from enum import StrEnum

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

from app.schemas.artifact import UserSummary
from app.schemas.common import UtcJsonModel


class CompetitionStatus(StrEnum):
    UPCOMING = "UPCOMING"
    ONGOING = "ONGOING"
    ENDED = "ENDED"


def _normalize_utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        raise ValueError("must include a UTC offset")
    return value.astimezone(UTC)


class CompetitionCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str = Field(min_length=1, max_length=200)
    summary: str = Field(min_length=1, max_length=500)
    rules_markdown: str = Field(min_length=1, max_length=100_000)
    start_at: datetime
    end_at: datetime

    @field_validator("title", "summary", "rules_markdown")
    @classmethod
    def strip_required_text(cls, value: str) -> str:
        value = value.strip()
        if not value:
            raise ValueError("must not be blank")
        return value

    @field_validator("start_at", "end_at")
    @classmethod
    def normalize_time(cls, value: datetime) -> datetime:
        return _normalize_utc(value)

    @model_validator(mode="after")
    def validate_window(self) -> CompetitionCreate:
        if self.start_at >= self.end_at:
            raise ValueError("start_at must be earlier than end_at")
        return self


class CompetitionUpdate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str | None = Field(default=None, min_length=1, max_length=200)
    summary: str | None = Field(default=None, min_length=1, max_length=500)
    rules_markdown: str | None = Field(default=None, min_length=1, max_length=100_000)
    start_at: datetime | None = None
    end_at: datetime | None = None

    @field_validator("title", "summary", "rules_markdown")
    @classmethod
    def strip_optional_text(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        if not value:
            raise ValueError("must not be blank")
        return value

    @field_validator("start_at", "end_at")
    @classmethod
    def normalize_time(cls, value: datetime | None) -> datetime | None:
        if value is None:
            return None
        return _normalize_utc(value)

    @model_validator(mode="after")
    def validate_window(self) -> CompetitionUpdate:
        if self.start_at is not None and self.end_at is not None and self.start_at >= self.end_at:
            raise ValueError("start_at must be earlier than end_at")
        return self


class CompetitionListItem(UtcJsonModel):
    id: int
    title: str
    summary: str
    creator: UserSummary
    status: CompetitionStatus
    start_at: datetime
    end_at: datetime
    created_at: datetime
    updated_at: datetime


class CompetitionRead(CompetitionListItem):
    rules_markdown: str


class CompetitionListResponse(BaseModel):
    items: list[CompetitionListItem]
    page: int
    page_size: int
    total: int
