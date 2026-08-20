"""Shared schema helpers for API serialization."""

from __future__ import annotations

from datetime import UTC, datetime

from pydantic import BaseModel, field_serializer


class UtcJsonModel(BaseModel):
    """Serialize datetimes as UTC ISO strings with a Z suffix.

    SQLite returns naive datetimes; all persisted times are UTC by
    convention, so naive values are treated as UTC when serializing.
    """

    @field_serializer("*", when_used="json")
    def serialize_utc_datetime(self, value: object) -> object:
        if not isinstance(value, datetime):
            return value
        if value.tzinfo is None:
            value = value.replace(tzinfo=UTC)
        else:
            value = value.astimezone(UTC)
        return value.isoformat().replace("+00:00", "Z")
