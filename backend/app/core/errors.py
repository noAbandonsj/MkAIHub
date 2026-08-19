"""Application errors and the public error response contract."""

from __future__ import annotations

from typing import Any

from pydantic import BaseModel, ConfigDict


class ErrorResponse(BaseModel):
    """Uniform error body returned by all API exception handlers."""

    model_config = ConfigDict(extra="forbid")

    code: str
    message: str
    details: Any | None = None


class AppError(Exception):
    """A safe, expected error that can be exposed to an API client."""

    def __init__(
        self,
        code: str,
        message: str,
        *,
        status_code: int = 400,
        details: Any | None = None,
    ) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.status_code = status_code
        self.details = details
