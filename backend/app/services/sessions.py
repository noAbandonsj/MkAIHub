"""Creation, lookup support, and revocation of server-side sessions."""

from __future__ import annotations

import secrets
from datetime import UTC, datetime, timedelta

from sqlalchemy import update
from sqlalchemy.orm import Session

from app.models import User, UserSession
from app.services.security import hash_session_token


SESSION_TOKEN_BYTES = 32


def utcnow() -> datetime:
    """Return the current UTC timestamp."""

    return datetime.now(UTC)


def create_session(db: Session, user: User, ttl_hours: int) -> tuple[str, UserSession]:
    """Create a high-entropy raw token and store only its SHA-256 hash."""

    raw_token = secrets.token_urlsafe(SESSION_TOKEN_BYTES)
    now = utcnow()
    session = UserSession(
        user_id=user.id,
        token_hash=hash_session_token(raw_token),
        expires_at=now + timedelta(hours=ttl_hours),
        last_seen_at=now,
        created_at=now,
    )
    db.add(session)
    db.flush()
    return raw_token, session


def revoke_session(session: UserSession, *, at: datetime | None = None) -> None:
    """Mark one session revoked without committing the surrounding transaction."""

    session.revoked_at = at or utcnow()


def revoke_all_sessions(db: Session, user_id: int, *, at: datetime | None = None) -> int:
    """Revoke every currently active session belonging to a user."""

    resolved_at = at or utcnow()
    result = db.execute(
        update(UserSession)
        .where(UserSession.user_id == user_id, UserSession.revoked_at.is_(None))
        .values(revoked_at=resolved_at)
    )
    return int(result.rowcount or 0)

