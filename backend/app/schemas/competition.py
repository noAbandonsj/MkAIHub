"""API schemas for competitions."""

from __future__ import annotations

from datetime import UTC, datetime
from decimal import Decimal
from enum import StrEnum

from pydantic import BaseModel, ConfigDict, Field, field_serializer, field_validator, model_validator

from app.schemas.artifact import UserSummary
from app.schemas.common import UtcJsonModel


class CompetitionStatus(StrEnum):
    UPCOMING = "UPCOMING"
    ONGOING = "ONGOING"
    ENDED = "ENDED"


class CompetitionLifecycle(StrEnum):
    DRAFT = "DRAFT"
    PUBLISHED = "PUBLISHED"
    RESULT_PUBLISHED = "RESULT_PUBLISHED"
    ARCHIVED = "ARCHIVED"


class RegistrationStatus(StrEnum):
    REGISTERED = "REGISTERED"
    CANCELLED = "CANCELLED"


def _normalize_utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        raise ValueError("must include a UTC offset")
    return value.astimezone(UTC)


def _score_2(value: Decimal) -> str:
    return f"{value:.2f}"


def _score_4(value: Decimal) -> str:
    return f"{value:.4f}"


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


class CompetitionRegistrationSummary(UtcJsonModel):
    id: int
    status: RegistrationStatus
    registered_at: datetime
    cancelled_at: datetime | None


class CompetitionListItem(UtcJsonModel):
    id: int
    title: str
    summary: str
    creator: UserSummary
    status: CompetitionStatus
    lifecycle_status: CompetitionLifecycle
    start_at: datetime
    end_at: datetime
    created_at: datetime
    updated_at: datetime


class CompetitionRead(CompetitionListItem):
    rules_markdown: str
    task_count: int = 0
    registration_count: int = 0
    my_registration: CompetitionRegistrationSummary | None = None
    results_published: bool = False


class CompetitionListResponse(BaseModel):
    items: list[CompetitionListItem]
    page: int
    page_size: int
    total: int


class CompetitionRegistrationRead(UtcJsonModel):
    id: int
    competition_id: int
    user: UserSummary
    status: RegistrationStatus
    registered_at: datetime
    cancelled_at: datetime | None


class CompetitionRegistrationListResponse(BaseModel):
    items: list[CompetitionRegistrationRead]


class CompetitionTaskSubmissionSummary(UtcJsonModel):
    id: int
    artifact_id: int
    artifact_title: str
    round_no: int
    status: str
    is_current: bool
    submitted_at: datetime


class CompetitionTaskRead(UtcJsonModel):
    id: int
    title: str
    description: str
    creator: UserSummary
    status: str
    required: bool
    sort_order: int
    max_score: Decimal
    weight: Decimal
    deadline_at: datetime | None
    effective_deadline_at: datetime
    my_submission: CompetitionTaskSubmissionSummary | None = None
    current_submission_count: int | None = None
    reviewed_count: int | None = None
    created_at: datetime
    updated_at: datetime

    @field_serializer("max_score", when_used="json")
    @classmethod
    def serialize_max_score(cls, value: Decimal) -> str:
        return _score_2(value)

    @field_serializer("weight", when_used="json")
    @classmethod
    def serialize_weight(cls, value: Decimal) -> str:
        return _score_2(value)


class CompetitionTaskListResponse(BaseModel):
    items: list[CompetitionTaskRead]


class CompetitionResultRow(UtcJsonModel):
    registration_id: int
    user: UserSummary
    total_score: Decimal
    rank: int
    award: str | None

    @field_serializer("total_score", when_used="json")
    @classmethod
    def serialize_total_score(cls, value: Decimal) -> str:
        return _score_4(value)


class CompetitionResultsRead(UtcJsonModel):
    competition_id: int
    published_by: UserSummary | None
    published_at: datetime | None
    items: list[CompetitionResultRow]


class CompetitionTaskCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str = Field(min_length=1, max_length=200)
    description: str = Field(min_length=1, max_length=100_000)
    deadline_at: datetime | None = None
    required: bool
    sort_order: int = Field(ge=0, le=9_999)
    max_score: Decimal = Field(gt=0, max_digits=6, decimal_places=2)
    weight: Decimal = Field(gt=0, max_digits=5, decimal_places=2)

    @field_validator("title", "description")
    @classmethod
    def strip_required_text(cls, value: str) -> str:
        value = value.strip()
        if not value:
            raise ValueError("must not be blank")
        return value

    @field_validator("deadline_at")
    @classmethod
    def normalize_deadline(cls, value: datetime | None) -> datetime | None:
        if value is None:
            return None
        return _normalize_utc(value)


class CompetitionTaskUpdate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str | None = Field(default=None, min_length=1, max_length=200)
    description: str | None = Field(default=None, min_length=1, max_length=100_000)
    deadline_at: datetime | None = None
    required: bool | None = None
    sort_order: int | None = Field(default=None, ge=0, le=9_999)
    max_score: Decimal | None = Field(default=None, gt=0, max_digits=6, decimal_places=2)
    weight: Decimal | None = Field(default=None, gt=0, max_digits=5, decimal_places=2)

    @field_validator("title", "description")
    @classmethod
    def strip_optional_text(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        if not value:
            raise ValueError("must not be blank")
        return value

    @field_validator("deadline_at")
    @classmethod
    def normalize_deadline(cls, value: datetime | None) -> datetime | None:
        if value is None:
            return None
        return _normalize_utc(value)


class CompetitionReviewRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    raw_score: Decimal = Field(ge=0, max_digits=6, decimal_places=2)
    comment: str | None = Field(default=None, max_length=2_000)

    @field_validator("comment")
    @classmethod
    def strip_comment(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        return value or None


class CompetitionAwardInput(BaseModel):
    model_config = ConfigDict(extra="forbid")

    registration_id: int
    award: str = Field(min_length=1, max_length=200)

    @field_validator("award")
    @classmethod
    def strip_award(cls, value: str) -> str:
        value = value.strip()
        if not value:
            raise ValueError("must not be blank")
        return value


class PublishResultsRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    awards: list[CompetitionAwardInput] | None = None
