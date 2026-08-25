"""API schemas for tasks."""

from __future__ import annotations

from datetime import UTC, datetime
from decimal import Decimal
from enum import StrEnum

from pydantic import BaseModel, ConfigDict, Field, field_serializer, field_validator

from app.schemas.artifact import UserSummary
from app.schemas.common import UtcJsonModel


class TaskStatus(StrEnum):
    OPEN = "OPEN"
    IN_PROGRESS = "IN_PROGRESS"
    REVIEWING = "REVIEWING"
    COMPLETED = "COMPLETED"
    CLOSED = "CLOSED"


TERMINAL_TASK_STATUSES = (TaskStatus.COMPLETED, TaskStatus.CLOSED)


class ParticipantStatus(StrEnum):
    ACTIVE = "ACTIVE"
    LEFT = "LEFT"


class SubmissionStatus(StrEnum):
    SUBMITTED = "SUBMITTED"
    REVISION_REQUIRED = "REVISION_REQUIRED"
    ACCEPTED = "ACCEPTED"
    REJECTED = "REJECTED"


class TaskCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str = Field(min_length=1, max_length=200)
    description: str = Field(min_length=1, max_length=100_000)
    deadline_at: datetime | None = None

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
        if value.tzinfo is None:
            raise ValueError("must include a UTC offset")
        return value.astimezone(UTC)


class TaskUpdate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str | None = Field(default=None, min_length=1, max_length=200)
    description: str | None = Field(default=None, min_length=1, max_length=100_000)
    deadline_at: datetime | None = None

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
        if value.tzinfo is None:
            raise ValueError("must include a UTC offset")
        return value.astimezone(UTC)


class TaskListItem(UtcJsonModel):
    id: int
    title: str
    creator: UserSummary
    status: TaskStatus
    competition_id: int | None = None
    competition_title: str | None = None
    deadline_at: datetime | None
    completed_at: datetime | None
    closed_at: datetime | None
    created_at: datetime
    updated_at: datetime


class TaskParticipantRead(UtcJsonModel):
    id: int
    task_id: int
    user: UserSummary
    status: ParticipantStatus
    joined_at: datetime
    left_at: datetime | None


class TaskRead(TaskListItem):
    description: str
    my_participation: TaskParticipantRead | None = None
    participant_count: int = 0
    submission_count: int = 0


class TaskListResponse(BaseModel):
    items: list[TaskListItem]
    page: int
    page_size: int
    total: int


class TaskParticipantListResponse(BaseModel):
    items: list[TaskParticipantRead]


class SubmissionArtifactSummary(BaseModel):
    id: int
    title: str
    status: str


class TaskCompetitionSummary(BaseModel):
    id: int
    title: str
    lifecycle_status: str


class SubmissionTaskSummary(BaseModel):
    id: int
    title: str
    status: TaskStatus
    competition: TaskCompetitionSummary | None = None


class SubmissionReviewSummary(UtcJsonModel):
    raw_score: Decimal
    comment: str | None
    reviewed_at: datetime
    reviewer: UserSummary

    @field_serializer("raw_score", when_used="json")
    @classmethod
    def serialize_raw_score(cls, value: Decimal) -> str:
        return f"{value:.2f}"


class TaskSubmissionRead(UtcJsonModel):
    id: int
    task_id: int
    participant_id: int
    participant: UserSummary
    artifact: SubmissionArtifactSummary
    round_no: int
    note: str | None
    status: SubmissionStatus
    is_current: bool
    submitted_at: datetime
    revision_requested_at: datetime | None
    decided_at: datetime | None
    decider: UserSummary | None = None
    decision_note: str | None
    task: SubmissionTaskSummary | None = None
    competition_review: SubmissionReviewSummary | None = None
    competition_rank: int | None = None
    competition_award: str | None = None


class TaskSubmissionListResponse(BaseModel):
    items: list[TaskSubmissionRead]
    page: int
    page_size: int
    total: int


class ArtifactTaskSourceListResponse(BaseModel):
    items: list[TaskSubmissionRead]


class TaskSubmissionCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    artifact_id: int
    note: str | None = Field(default=None, max_length=2_000)

    @field_validator("note")
    @classmethod
    def strip_note(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        return value or None


def _require_nonblank_note(value: str) -> str:
    value = value.strip()
    if not value:
        raise ValueError("must not be blank")
    return value


class SubmissionRevisionRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    note: str = Field(min_length=1, max_length=2_000)

    @field_validator("note")
    @classmethod
    def strip_note(cls, value: str) -> str:
        return _require_nonblank_note(value)


class SubmissionAcceptRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    note: str | None = Field(default=None, max_length=2_000)

    @field_validator("note")
    @classmethod
    def strip_note(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        return value or None


class SubmissionRejectRequest(SubmissionRevisionRequest):
    pass
