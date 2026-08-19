"""FastAPI application factory and process entry point."""

from __future__ import annotations

import logging
import secrets
import time
from collections.abc import Callable
from typing import Any

from fastapi import FastAPI, HTTPException, Request
from fastapi.encoders import jsonable_encoder
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from fastapi.staticfiles import StaticFiles
from starlette.exceptions import HTTPException as StarletteHTTPException

from app import __version__
from app.api.health import router as health_router
from app.api.v1 import router as v1_router
from app.core.config import Settings, get_settings
from app.core.errors import AppError, ErrorResponse
from app.core.logging import configure_logging
from app.db.session import create_engine_from_url


logger = logging.getLogger("mkaihub.http")


class SPAStaticFiles(StaticFiles):
    """Serve ``index.html`` for client-side routes while preserving API 404s."""

    async def get_response(self, path: str, scope: dict[str, Any]) -> Any:
        try:
            return await super().get_response(path, scope)
        except StarletteHTTPException as exc:
            if exc.status_code == 404 and not path.startswith("api/"):
                return await super().get_response("index.html", scope)
            raise


def _json_error(code: str, message: str, details: Any | None, status_code: int) -> JSONResponse:
    body = ErrorResponse(code=code, message=message, details=details).model_dump(exclude_none=True)
    return JSONResponse(status_code=status_code, content=body)


def _http_exception_payload(detail: Any) -> tuple[str, str, Any | None]:
    if isinstance(detail, dict):
        code = str(detail.get("code", "HTTP_ERROR"))
        message = str(detail.get("message", "Request failed"))
        details = detail.get("details")
        return code, message, details
    return "HTTP_ERROR", str(detail), None


def _safe_validation_details(errors: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Remove submitted values and exception contexts from validation errors."""

    safe_errors: list[dict[str, Any]] = []
    for error in errors:
        safe_errors.append({key: value for key, value in error.items() if key not in {"input", "ctx"}})
    return safe_errors


def create_app(settings: Settings | None = None) -> FastAPI:
    """Build an application instance, allowing isolated test settings."""

    resolved_settings = settings or get_settings()
    configure_logging(resolved_settings.log_level)
    engine = create_engine_from_url(resolved_settings.database_url)

    application = FastAPI(
        title=resolved_settings.app_name,
        version=__version__,
        description="MkAIHub internal AI sharing platform API",
    )
    application.state.settings = resolved_settings
    application.state.db_engine = engine
    configured_secret = resolved_settings.app_secret_key
    # Production settings reject a missing secret.  Development/test apps get
    # a fresh in-memory secret rather than silently using a reusable default.
    application.state.csrf_secret = (
        configured_secret.get_secret_value().encode("utf-8")
        if configured_secret
        else secrets.token_bytes(32)
    )

    if resolved_settings.frontend_origins:
        application.add_middleware(
            CORSMiddleware,
            allow_origins=resolved_settings.frontend_origins,
            allow_credentials=True,
            allow_methods=["*"],
            allow_headers=["*"],
        )

    @application.exception_handler(AppError)
    async def app_error_handler(_request: Request, exc: AppError) -> JSONResponse:
        return _json_error(exc.code, exc.message, exc.details, exc.status_code)

    @application.exception_handler(RequestValidationError)
    async def validation_error_handler(_request: Request, exc: RequestValidationError) -> JSONResponse:
        return _json_error(
            "VALIDATION_ERROR",
            "Request validation failed",
            jsonable_encoder(_safe_validation_details(exc.errors())),
            422,
        )

    @application.exception_handler(StarletteHTTPException)
    async def http_error_handler(_request: Request, exc: StarletteHTTPException) -> JSONResponse:
        code, message, details = _http_exception_payload(exc.detail)
        return _json_error(code, message, details, exc.status_code)

    @application.exception_handler(Exception)
    async def unhandled_error_handler(_request: Request, exc: Exception) -> JSONResponse:
        logger.exception("Unhandled application error", exc_info=exc)
        return _json_error("INTERNAL_SERVER_ERROR", "Internal server error", None, 500)

    @application.middleware("http")
    async def request_logging_middleware(request: Request, call_next: Callable[..., Any]) -> Any:
        started = time.perf_counter()
        response = await call_next(request)
        duration_ms = round((time.perf_counter() - started) * 1000, 2)
        logger.info(
            "HTTP request",
            extra={
                "method": request.method,
                "path": request.url.path,
                "status_code": response.status_code,
                "duration_ms": duration_ms,
            },
        )
        return response

    application.include_router(health_router, prefix="/api")
    application.include_router(v1_router, prefix="/api")

    @application.api_route(
        "/api/{path:path}",
        methods=["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD"],
        include_in_schema=False,
    )
    async def unknown_api_route(path: str) -> None:
        del path
        raise HTTPException(status_code=404, detail={"code": "NOT_FOUND", "message": "API route not found"})

    frontend_dist = resolved_settings.frontend_dist
    if frontend_dist.is_dir() and (frontend_dist / "index.html").is_file():
        application.mount("/", SPAStaticFiles(directory=frontend_dist, html=True), name="frontend")
    else:

        @application.get("/", include_in_schema=False)
        async def backend_root() -> dict[str, str]:
            return {"service": resolved_settings.app_name, "health": "/api/health"}

    return application


app = create_app()
