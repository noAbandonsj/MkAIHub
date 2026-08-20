"""API schemas for tasks."""

from __future__ import annotations

from datetime import UTC, datetime
from enum import StrEnum

from pydantic import BaseModel, ConfigDict, Field, field_validator

from app.schemas.artifact import UserSummary
from app.schemas.common import UtcJsonModel


class TaskStatus(StrEnum):
    OPEN = "OPEN"
    COMPLETED = "COMPLETED"
    CLOSED = "CLOSED"


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
    deadline_at: datetime | None
    completed_at: datetime | None
    closed_at: datetime | None
    created_at: datetime
    updated_at: datetime


class TaskRead(TaskListItem):
    description: str


class TaskListResponse(BaseModel):
    items: list[TaskListItem]
    page: int
    page_size: int
    total: int
