"""Small JSON logger setup used by the application and request middleware."""

from __future__ import annotations

import json
import logging
import logging.config
import sys
from datetime import UTC, datetime
from typing import Any


# Standard LogRecord factory attributes; anything else on a record was passed
# via ``logger.info(..., extra={...})`` and belongs in the JSON payload.
_RESERVED_RECORD_FIELDS = frozenset(
    {
        "name",
        "msg",
        "args",
        "levelname",
        "levelno",
        "pathname",
        "filename",
        "module",
        "exc_info",
        "exc_text",
        "stack_info",
        "lineno",
        "funcName",
        "created",
        "msecs",
        "relativeCreated",
        "thread",
        "threadName",
        "processName",
        "process",
        "taskName",
        "message",
        "asctime",
    }
)


class JsonFormatter(logging.Formatter):
    """Format records as one structured JSON object per line."""

    def format(self, record: logging.LogRecord) -> str:
        payload: dict[str, Any] = {
            "timestamp": datetime.now(UTC).isoformat(),
            "level": record.levelname,
            "logger": record.name,
            "message": record.getMessage(),
        }
        if record.exc_info:
            payload["exception"] = self.formatException(record.exc_info)
        for key, value in record.__dict__.items():
            if key not in _RESERVED_RECORD_FIELDS and key not in payload and not key.startswith("_"):
                payload[key] = value
        return json.dumps(payload, ensure_ascii=False, default=str)


def configure_logging(level: str = "INFO") -> None:
    """Install a predictable JSON stream handler for application logs."""

    resolved_level = getattr(logging, level.upper(), logging.INFO)
    root_logger = logging.getLogger()
    root_logger.setLevel(resolved_level)
    application_logger = logging.getLogger("mkaihub")
    application_logger.setLevel(resolved_level)
    application_logger.propagate = False
    if not any(isinstance(handler.formatter, JsonFormatter) for handler in application_logger.handlers):
        handler = logging.StreamHandler(sys.stdout)
        handler.setFormatter(JsonFormatter())
        application_logger.addHandler(handler)


logger = logging.getLogger("mkaihub")


def log_admin_action(
    action: str,
    *,
    actor_id: int,
    target_type: str,
    target_id: int,
    **details: Any,
) -> None:
    """Emit one structured audit record for a privileged management action."""

    extra: dict[str, Any] = {
        "action": f"admin.{action}",
        "actor_id": actor_id,
        "target_type": target_type,
        "target_id": target_id,
    }
    extra.update(details)
    logger.info("admin.%s", action, extra=extra)
