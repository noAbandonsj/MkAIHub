"""Authentication and current-user endpoints."""

from __future__ import annotations

from fastapi import APIRouter, Depends, Request, Response, status
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app.api.v1.deps import (
    AuthContext,
    cookie_secure,
    derive_csrf_token,
    get_current_auth,
    require_csrf,
)
from app.core.errors import AppError
from app.db.session import get_db
from app.models import User
from app.schemas.auth import ChangePasswordRequest, CsrfTokenResponse, LoginRequest, LoginResponse, UserRead
from app.services.security import hash_password, verify_password
from app.services.sessions import create_session, revoke_all_sessions, revoke_session, utcnow


router = APIRouter(prefix="/auth", tags=["auth"])


def _invalid_credentials() -> AppError:
    return AppError("INVALID_CREDENTIALS", "Username or password is incorrect", status_code=401)


def _set_session_cookie(response: Response, request: Request, raw_token: str) -> None:
    settings = request.app.state.settings
    response.set_cookie(
        key=settings.session_cookie_name,
        value=raw_token,
        max_age=settings.session_ttl_hours * 60 * 60,
        httponly=True,
        secure=cookie_secure(request),
        samesite="lax",
        path="/",
    )


def _clear_session_cookie(response: Response, request: Request) -> None:
    settings = request.app.state.settings
    response.delete_cookie(
        key=settings.session_cookie_name,
        secure=cookie_secure(request),
        httponly=True,
        samesite="lax",
        path="/",
    )


@router.post("/login", response_model=LoginResponse, summary="Log in")
def login(
    payload: LoginRequest,
    request: Request,
    response: Response,
    db: Session = Depends(get_db),
) -> LoginResponse:
    """Authenticate a user and issue a new server-side session cookie."""

    user = db.scalar(select(User).where(User.username == payload.username))
    password_ok = user is not None and verify_password(payload.password, user.password_hash)
    if user is None or not password_ok or not user.is_active:
        raise _invalid_credentials()

    now = utcnow()
    user.last_login_at = now
    raw_token, _session = create_session(db, user, request.app.state.settings.session_ttl_hours)
    try:
        db.commit()
    except IntegrityError:
        db.rollback()
        raise AppError("AUTHENTICATION_FAILED", "Unable to create a session", status_code=503) from None

    _set_session_cookie(response, request, raw_token)
    return LoginResponse(user=user)


@router.post("/logout", status_code=status.HTTP_204_NO_CONTENT, summary="Log out")
def logout(
    request: Request,
    response: Response,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> None:
    """Revoke the current session and clear the browser cookie."""

    revoke_session(auth.session)
    db.commit()
    _clear_session_cookie(response, request)


@router.get("/me", response_model=UserRead, summary="Get current user")
def me(auth: AuthContext = Depends(get_current_auth)) -> User:
    return auth.user


@router.get("/csrf-token", response_model=CsrfTokenResponse, summary="Get CSRF token")
def csrf_token(request: Request, auth: AuthContext = Depends(get_current_auth)) -> CsrfTokenResponse:
    return CsrfTokenResponse(csrf_token=derive_csrf_token(request, auth.raw_token))


@router.post("/change-password", status_code=status.HTTP_204_NO_CONTENT, summary="Change password")
def change_password(
    payload: ChangePasswordRequest,
    request: Request,
    response: Response,
    auth: AuthContext = Depends(require_csrf),
    db: Session = Depends(get_db),
) -> None:
    """Change the current user's password and revoke every session."""

    if not verify_password(payload.current_password, auth.user.password_hash):
        raise AppError("CURRENT_PASSWORD_INVALID", "Current password is incorrect", status_code=400)
    if payload.current_password == payload.new_password:
        raise AppError("PASSWORD_UNCHANGED", "New password must differ from the current password", status_code=400)

    auth.user.password_hash = hash_password(payload.new_password)
    revoke_all_sessions(db, auth.user.id)
    db.commit()
    _clear_session_cookie(response, request)
