"""API schemas for artifacts, files, comments, and explore."""

from __future__ import annotations

from datetime import datetime
from enum import StrEnum

from pydantic import BaseModel, ConfigDict, Field, field_validator

from app.schemas.common import UtcJsonModel


class ArtifactStatus(StrEnum):
    DRAFT = "DRAFT"
    PUBLISHED = "PUBLISHED"
    ARCHIVED = "ARCHIVED"


class CommentStatus(StrEnum):
    VISIBLE = "VISIBLE"
    HIDDEN = "HIDDEN"


class UserSummary(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    username: str
    display_name: str


class FileRead(UtcJsonModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    original_name: str
    extension: str
    mime_type: str
    size_bytes: int
    uploader_id: int
    created_at: datetime


class ArtifactCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str = Field(min_length=1, max_length=200)
    summary: str = Field(min_length=1, max_length=500)
    content_markdown: str = Field(min_length=1, max_length=100_000)
    file_ids: list[int] = Field(default_factory=list, max_length=10)

    @field_validator("title", "summary", "content_markdown")
    @classmethod
    def strip_required_text(cls, value: str) -> str:
        value = value.strip()
        if not value:
            raise ValueError("must not be blank")
        return value

    @field_validator("file_ids")
    @classmethod
    def unique_file_ids(cls, value: list[int]) -> list[int]:
        if any(file_id <= 0 for file_id in value):
            raise ValueError("file ids must be positive")
        if len(value) != len(set(value)):
            raise ValueError("file ids must be unique")
        return value


class ArtifactUpdate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str | None = Field(default=None, min_length=1, max_length=200)
    summary: str | None = Field(default=None, min_length=1, max_length=500)
    content_markdown: str | None = Field(default=None, min_length=1, max_length=100_000)
    file_ids: list[int] | None = Field(default=None, max_length=10)

    @field_validator("title", "summary", "content_markdown")
    @classmethod
    def strip_optional_text(cls, value: str | None) -> str | None:
        if value is None:
            return None
        value = value.strip()
        if not value:
            raise ValueError("must not be blank")
        return value

    @field_validator("file_ids")
    @classmethod
    def unique_optional_file_ids(cls, value: list[int] | None) -> list[int] | None:
        if value is None:
            return None
        if any(file_id <= 0 for file_id in value):
            raise ValueError("file ids must be positive")
        if len(value) != len(set(value)):
            raise ValueError("file ids must be unique")
        return value


class ArtifactListItem(UtcJsonModel):
    id: int
    title: str
    summary: str
    author: UserSummary
    status: ArtifactStatus
    attachment_count: int
    published_at: datetime | None
    created_at: datetime
    updated_at: datetime


class ArtifactRead(ArtifactListItem):
    content_markdown: str
    archived_at: datetime | None
    files: list[FileRead]


class ArtifactListResponse(BaseModel):
    items: list[ArtifactListItem]
    page: int
    page_size: int
    total: int


class CommentCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    content: str = Field(min_length=1, max_length=2_000)

    @field_validator("content")
    @classmethod
    def strip_content(cls, value: str) -> str:
        value = value.strip()
        if not value:
            raise ValueError("must not be blank")
        return value


class CommentRead(UtcJsonModel):
    id: int
    artifact_id: int | None = None
    issue_id: int | None = None
    author: UserSummary
    content: str
    status: CommentStatus
    created_at: datetime
    updated_at: datetime


class CommentListResponse(BaseModel):
    items: list[CommentRead]
    page: int
    page_size: int
    total: int


class ExploreResponse(BaseModel):
    latest_artifacts: list[ArtifactListItem]
