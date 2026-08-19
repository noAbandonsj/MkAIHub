"""Authentication and user-management request/response schemas."""

from __future__ import annotations

import re
from datetime import UTC, datetime
from enum import Enum
from typing import Any

from pydantic import BaseModel, ConfigDict, field_serializer, field_validator


USERNAME_PATTERN = re.compile(r"^[a-z0-9._-]+$")


class UserRole(str, Enum):
    """Roles supported by the first release."""

    EMPLOYEE = "EMPLOYEE"
    SYSTEM_ADMIN = "SYSTEM_ADMIN"


def _normalize_username(value: Any) -> str:
    if not isinstance(value, str):
        raise ValueError("Username must be a string")
    normalized = value.strip().lower()
    if not 3 <= len(normalized) <= 64 or USERNAME_PATTERN.fullmatch(normalized) is None:
        raise ValueError("Username must be 3-64 characters using letters, digits, '.', '_' or '-'")
    return normalized


def _normalize_display_name(value: Any) -> str:
    if not isinstance(value, str):
        raise ValueError("Display name must be a string")
    normalized = value.strip()
    if not 1 <= len(normalized) <= 100:
        raise ValueError("Display name must be 1-100 characters")
    return normalized


def _validate_password(value: Any) -> str:
    if not isinstance(value, str) or not 8 <= len(value) <= 128:
        raise ValueError("Password must be 8-128 characters")
    return value


class LoginRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    username: str
    password: str

    _username = field_validator("username", mode="before")(_normalize_username)
    _password = field_validator("password", mode="after")(_validate_password)


class ChangePasswordRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    current_password: str
    new_password: str

    _current_password = field_validator("current_password", mode="after")(_validate_password)
    _new_password = field_validator("new_password", mode="after")(_validate_password)


class UserRead(BaseModel):
    model_config = ConfigDict(from_attributes=True, extra="forbid")

    id: int
    username: str
    display_name: str
    role: UserRole
    is_active: bool
    last_login_at: datetime | None
    created_at: datetime
    updated_at: datetime

    @field_serializer("last_login_at", "created_at", "updated_at", when_used="json")
    def serialize_datetime(self, value: datetime | None) -> str | None:
        if value is None:
            return None
        if value.tzinfo is None:
            value = value.replace(tzinfo=UTC)
        else:
            value = value.astimezone(UTC)
        return value.isoformat().replace("+00:00", "Z")


class CsrfTokenResponse(BaseModel):
    model_config = ConfigDict(extra="forbid")

    csrf_token: str


class LoginResponse(BaseModel):
    model_config = ConfigDict(extra="forbid")

    user: UserRead


class UserListResponse(BaseModel):
    model_config = ConfigDict(extra="forbid")

    items: list[UserRead]
    page: int
    page_size: int
    total: int


class AdminUserCreate(BaseModel):
    model_config = ConfigDict(extra="forbid")

    username: str
    display_name: str
    password: str
    role: UserRole

    _username = field_validator("username", mode="before")(_normalize_username)
    _display_name = field_validator("display_name", mode="before")(_normalize_display_name)
    _password = field_validator("password", mode="after")(_validate_password)


class AdminUserPatch(BaseModel):
    model_config = ConfigDict(extra="forbid")

    display_name: str | None = None
    role: UserRole | None = None
    is_active: bool | None = None

    @field_validator("display_name", mode="before")
    @classmethod
    def validate_optional_display_name(cls, value: Any) -> str | None:
        return None if value is None else _normalize_display_name(value)


class AdminResetPassword(BaseModel):
    model_config = ConfigDict(extra="forbid")

    new_password: str

    _new_password = field_validator("new_password", mode="after")(_validate_password)
