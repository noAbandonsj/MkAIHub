from __future__ import annotations

from datetime import UTC, datetime, timedelta
from fastapi.testclient import TestClient
from sqlalchemy import select

from app.db.session import create_engine_from_url, session_factory
from app.models import User, UserSession
from app.services.security import hash_password, hash_session_token


def seed_user(settings, *, username: str, role: str, password: str = "password1") -> int:
    engine = create_engine_from_url(settings.database_url)
    try:
        with session_factory(engine)() as db:
            user = User(
                username=username,
                display_name=username.title(),
                password_hash=hash_password(password),
                role=role,
                is_active=True,
            )
            db.add(user)
            db.commit()
            db.refresh(user)
            return user.id
    finally:
        engine.dispose()


def csrf(client: TestClient) -> str:
    response = client.get("/api/v1/auth/csrf-token")
    assert response.status_code == 200
    return response.json()["csrf_token"]


def test_login_me_logout_and_cookie_contract(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="admin", role="SYSTEM_ADMIN")

    response = client.post("/api/v1/auth/login", json={"username": "ADMIN", "password": "password1"})
    assert response.status_code == 200
    assert response.json()["user"]["username"] == "admin"
    cookie = response.headers["set-cookie"]
    assert "HttpOnly" in cookie
    assert "SameSite=lax" in cookie
    assert "Secure" not in cookie

    assert client.get("/api/v1/auth/me").status_code == 200
    token = csrf(client)
    response = client.post("/api/v1/auth/logout", headers={"X-CSRF-Token": token})
    assert response.status_code == 204
    assert client.get("/api/v1/auth/me").status_code == 401


def test_wrong_password_and_inactive_user_cannot_login(client: TestClient, test_settings) -> None:
    user_id = seed_user(test_settings, username="admin", role="SYSTEM_ADMIN")
    assert client.post("/api/v1/auth/login", json={"username": "admin", "password": "wrongpass"}).status_code == 401

    engine = create_engine_from_url(test_settings.database_url)
    try:
        with session_factory(engine)() as db:
            user = db.get(User, user_id)
            assert user is not None
            user.is_active = False
            db.commit()
    finally:
        engine.dispose()
    assert client.post("/api/v1/auth/login", json={"username": "admin", "password": "password1"}).status_code == 401


def test_csrf_and_validation_do_not_echo_password(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="admin", role="SYSTEM_ADMIN")
    client.post("/api/v1/auth/login", json={"username": "admin", "password": "password1"})

    response = client.post("/api/v1/auth/logout")
    assert response.status_code == 403
    response = client.post(
        "/api/v1/auth/change-password",
        headers={"X-CSRF-Token": csrf(client)},
        json={"current_password": "short", "new_password": "short"},
    )
    assert response.status_code == 422
    assert "short" not in response.text
    assert "input" not in response.text


def test_expired_and_revoked_sessions_are_rejected(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="admin", role="SYSTEM_ADMIN")
    client.post("/api/v1/auth/login", json={"username": "admin", "password": "password1"})
    raw_token = client.cookies.get(test_settings.session_cookie_name)
    assert raw_token
    engine = create_engine_from_url(test_settings.database_url)
    try:
        with session_factory(engine)() as db:
            session = db.scalar(select(UserSession).where(UserSession.token_hash == hash_session_token(raw_token)))
            assert session is not None
            session.expires_at = datetime.now(UTC) - timedelta(minutes=1)
            db.commit()
    finally:
        engine.dispose()
    assert client.get("/api/v1/auth/me").status_code == 401


def test_change_password_revokes_all_sessions(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="admin", role="SYSTEM_ADMIN")
    client.post("/api/v1/auth/login", json={"username": "admin", "password": "password1"})
    other = TestClient(client.app)
    try:
        other.post("/api/v1/auth/login", json={"username": "admin", "password": "password1"})
        response = client.post(
            "/api/v1/auth/change-password",
            headers={"X-CSRF-Token": csrf(client)},
            json={"current_password": "password1", "new_password": "password2"},
        )
        assert response.status_code == 204
        assert client.get("/api/v1/auth/me").status_code == 401
        assert other.get("/api/v1/auth/me").status_code == 401
        response = other.post("/api/v1/auth/login", json={"username": "admin", "password": "password2"})
        assert response.status_code == 200
    finally:
        other.close()


def test_admin_crud_and_employee_forbidden(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="admin", role="SYSTEM_ADMIN")
    employee_id = seed_user(test_settings, username="employee", role="EMPLOYEE")
    client.post("/api/v1/auth/login", json={"username": "admin", "password": "password1"})
    header = {"X-CSRF-Token": csrf(client)}

    employee = TestClient(client.app)
    employee.post("/api/v1/auth/login", json={"username": "employee", "password": "password1"})
    assert employee.get("/api/v1/admin/users").status_code == 403
    employee.close()

    response = client.post(
        "/api/v1/admin/users",
        headers=header,
        json={"username": "New.User", "display_name": "New User", "password": "password2", "role": "EMPLOYEE"},
    )
    assert response.status_code == 201
    assert response.json()["username"] == "new.user"

    response = client.patch(f"/api/v1/admin/users/{employee_id}", headers=header, json={"is_active": False})
    assert response.status_code == 200
    assert response.json()["is_active"] is False
    response = client.post(
        f"/api/v1/admin/users/{employee_id}/reset-password",
        headers=header,
        json={"new_password": "password3"},
    )
    assert response.status_code == 204


def test_admin_cannot_deactivate_or_downgrade_self(client: TestClient, test_settings) -> None:
    admin_id = seed_user(test_settings, username="admin", role="SYSTEM_ADMIN")
    client.post("/api/v1/auth/login", json={"username": "admin", "password": "password1"})
    header = {"X-CSRF-Token": csrf(client)}
    assert client.patch(f"/api/v1/admin/users/{admin_id}", headers=header, json={"is_active": False}).status_code == 403
    assert client.patch(f"/api/v1/admin/users/{admin_id}", headers=header, json={"role": "EMPLOYEE"}).status_code == 403
