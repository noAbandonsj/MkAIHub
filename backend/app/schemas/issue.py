"""API schemas for issues."""

from __future__ import annotations

from datetime import datetime
from enum import StrEnum

from pydantic import BaseModel, ConfigDict, Field, field_validator

from app.schemas.artifact import UserSummary
from app.schemas.common import UtcJsonModel


class IssueStatus(StrEnum):
    OPEN = "OPEN"
    CLOSED = "CLOSED"


class IssueCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str = Field(min_length=1, max_length=200)
    description: str = Field(min_length=1, max_length=100_000)

    @field_validator("title", "description")
    @classmethod
    def strip_required_text(cls, value: str) -> str:
        value = value.strip()
        if not value:
            raise ValueError("must not be blank")
        return value


class IssueUpdate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str | None = Field(default=None, min_length=1, max_length=200)
    description: str | None = Field(default=None, min_length=1, max_length=100_000)

    @field_validator("title", "description")
    @classmethod
    def strip_optional_text(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        if not value:
            raise ValueError("must not be blank")
        return value


class IssueListItem(UtcJsonModel):
    id: int
    title: str
    author: UserSummary
    status: IssueStatus
    closed_at: datetime | None
    created_at: datetime
    updated_at: datetime


class IssueRead(IssueListItem):
    description: str


class IssueListResponse(BaseModel):
    items: list[IssueListItem]
    page: int
    page_size: int
    total: int
