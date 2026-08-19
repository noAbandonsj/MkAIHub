"""Authentication, authorization, and CSRF dependencies for v1 routes."""

from __future__ import annotations

import hashlib
import hmac
from dataclasses import dataclass
from datetime import UTC, datetime

from fastapi import Depends, Request
from sqlalchemy import select
from sqlalchemy.orm import Session, joinedload

from app.core.errors import AppError
from app.db.session import get_db
from app.models import User, UserSession
from app.schemas.auth import UserRole
from app.services.security import hash_session_token


SESSION_LAST_SEEN_THROTTLE_SECONDS = 300


@dataclass(slots=True)
class AuthContext:
    """Authenticated request context containing the user and current session."""

    user: User
    session: UserSession
    raw_token: str


def _as_utc(value: datetime) -> datetime:
    if value.tzinfo is None:
        return value.replace(tzinfo=UTC)
    return value.astimezone(UTC)


def _invalid_session() -> AppError:
    return AppError(
        "AUTHENTICATION_REQUIRED",
        "Authentication is required",
        status_code=401,
    )


def get_current_auth(request: Request, db: Session = Depends(get_db)) -> AuthContext:
    """Resolve and validate the HttpOnly session cookie."""

    settings = request.app.state.settings
    raw_token = request.cookies.get(settings.session_cookie_name)
    if not raw_token:
        raise _invalid_session()

    session = db.scalar(
        select(UserSession)
        .options(joinedload(UserSession.user))
        .where(UserSession.token_hash == hash_session_token(raw_token))
    )
    now = datetime.now(UTC)
    if session is None or session.revoked_at is not None:
        raise _invalid_session()
    if _as_utc(session.expires_at) <= now:
        raise _invalid_session()
    user = session.user
    if user is None or not user.is_active:
        raise _invalid_session()

    # last_seen is persisted at most every five minutes to keep authenticated
    # read traffic from becoming a write workload.
    if now.timestamp() - _as_utc(session.last_seen_at).timestamp() >= SESSION_LAST_SEEN_THROTTLE_SECONDS:
        session.last_seen_at = now
        db.commit()

    return AuthContext(user=user, session=session, raw_token=raw_token)


def get_current_user(auth: AuthContext = Depends(get_current_auth)) -> User:
    """Return the currently authenticated active user."""

    return auth.user


def _csrf_secret(request: Request) -> bytes:
    secret = getattr(request.app.state, "csrf_secret", None)
    if not isinstance(secret, bytes) or not secret:
        raise AppError(
            "SERVER_CONFIGURATION_ERROR",
            "Server security configuration is incomplete",
            status_code=500,
        )
    return secret


def derive_csrf_token(request: Request, raw_token: str) -> str:
    """Derive a session-bound CSRF token without storing another secret."""

    return hmac.new(_csrf_secret(request), raw_token.encode("utf-8"), hashlib.sha256).hexdigest()


def require_csrf(request: Request, auth: AuthContext = Depends(get_current_auth)) -> AuthContext:
    """Require the double-submit CSRF header for an authenticated write."""

    supplied = request.headers.get("X-CSRF-Token", "")
    expected = derive_csrf_token(request, auth.raw_token)
    if not supplied or not hmac.compare_digest(supplied, expected):
        raise AppError("CSRF_INVALID", "CSRF token is missing or invalid", status_code=403)
    return auth


def require_admin(auth: AuthContext = Depends(get_current_auth)) -> AuthContext:
    """Require the system administrator role."""

    if auth.user.role != UserRole.SYSTEM_ADMIN.value:
        raise AppError("FORBIDDEN", "System administrator permission is required", status_code=403)
    return auth


def require_admin_csrf(request: Request, auth: AuthContext = Depends(require_admin)) -> AuthContext:
    """Require both administrator authorization and a valid CSRF token."""

    supplied = request.headers.get("X-CSRF-Token", "")
    expected = derive_csrf_token(request, auth.raw_token)
    if not supplied or not hmac.compare_digest(supplied, expected):
        raise AppError("CSRF_INVALID", "CSRF token is missing or invalid", status_code=403)
    return auth


def cookie_secure(request: Request) -> bool:
    """Use Secure cookies in production while keeping local HTTP development usable."""

    return str(request.app.state.settings.app_env).lower() == "production"

