from __future__ import annotations

import logging

import pytest
from fastapi.testclient import TestClient

from app.core.logging import logger as app_logger
from test_artifacts import login, seed_user


class _AuditCapture(logging.Handler):
    def __init__(self) -> None:
        super().__init__()
        self.records: list[logging.LogRecord] = []

    def emit(self, record: logging.LogRecord) -> None:
        self.records.append(record)


@pytest.fixture
def audit_records():
    capture = _AuditCapture()
    previous_level = app_logger.level
    app_logger.setLevel(logging.INFO)
    app_logger.addHandler(capture)
    try:
        yield capture.records
    finally:
        app_logger.removeHandler(capture)
        app_logger.setLevel(previous_level)


def test_comment_hide_and_restore_visibility(client: TestClient, test_settings, audit_records) -> None:
    admin_id = seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    seed_user(test_settings, username="author")
    seed_user(test_settings, username="reader")

    author = TestClient(client.app)
    reader = TestClient(client.app)
    admin = TestClient(client.app)
    try:
        author_headers = login(author, "author")
        reader_headers = login(reader, "reader")
        admin_headers = login(admin, "boss")

        artifact = author.post(
            "/api/v1/artifacts",
            headers=author_headers,
            json={
                "title": "团队提示词实践",
                "summary": "一条可复用的内部提示词",
                "content_markdown": "# 内容",
                "file_ids": [],
            },
        ).json()
        assert author.post(f"/api/v1/artifacts/{artifact['id']}/publish", headers=author_headers).status_code == 200

        issue = reader.post(
            "/api/v1/issues",
            headers=reader_headers,
            json={"title": "导出功能异常", "description": "导出的文件缺少标题行。"},
        ).json()

        artifact_comment = reader.post(
            f"/api/v1/artifacts/{artifact['id']}/comments",
            headers=reader_headers,
            json={"content": "希望补充示例。"},
        ).json()
        issue_comment = author.post(
            f"/api/v1/issues/{issue['id']}/comments",
            headers=author_headers,
            json={"content": "已在新版本修复。"},
        ).json()

        # Only administrators may moderate comments.
        assert reader.post(
            f"/api/v1/admin/comments/{artifact_comment['id']}/hide",
            headers=reader_headers,
        ).status_code == 403
        assert admin.post("/api/v1/admin/comments/999/hide", headers=admin_headers).status_code == 404

        assert admin.post(
            f"/api/v1/admin/comments/{artifact_comment['id']}/hide", headers=admin_headers
        ).status_code == 204
        assert admin.post(
            f"/api/v1/admin/comments/{issue_comment['id']}/hide", headers=admin_headers
        ).status_code == 204

        # Employees no longer see the hidden comments, administrators still do.
        employee_artifact_comments = reader.get(
            f"/api/v1/artifacts/{artifact['id']}/comments", headers=reader_headers
        ).json()
        assert employee_artifact_comments["total"] == 0
        employee_issue_comments = reader.get(
            f"/api/v1/issues/{issue['id']}/comments", headers=reader_headers
        ).json()
        assert employee_issue_comments["total"] == 0

        admin_artifact_comments = admin.get(
            f"/api/v1/artifacts/{artifact['id']}/comments", headers=admin_headers
        ).json()
        assert admin_artifact_comments["total"] == 1
        assert admin_artifact_comments["items"][0]["status"] == "HIDDEN"
        admin_issue_comments = admin.get(f"/api/v1/issues/{issue['id']}/comments", headers=admin_headers).json()
        assert admin_issue_comments["items"][0]["status"] == "HIDDEN"

        assert admin.post(
            f"/api/v1/admin/comments/{artifact_comment['id']}/restore", headers=admin_headers
        ).status_code == 204
        restored = reader.get(
            f"/api/v1/artifacts/{artifact['id']}/comments", headers=reader_headers
        ).json()
        assert restored["total"] == 1
        assert restored["items"][0]["status"] == "VISIBLE"

        hide_records = [r for r in audit_records if getattr(r, "action", None) == "admin.comment.hide"]
        assert len(hide_records) == 2
        assert {r.target_id for r in hide_records} == {artifact_comment["id"], issue_comment["id"]}
        assert all(r.target_type == "comment" for r in hide_records)
        assert all(r.actor_id == admin_id for r in hide_records)
        restore_records = [r for r in audit_records if getattr(r, "action", None) == "admin.comment.restore"]
        assert len(restore_records) == 1
        assert restore_records[0].target_id == artifact_comment["id"]
    finally:
        author.close()
        reader.close()
        admin.close()


def test_admin_competition_and_user_actions_are_logged(client: TestClient, test_settings, audit_records) -> None:
    admin_id = seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    admin_headers = login(client, "boss")

    competition = client.post(
        "/api/v1/admin/competitions",
        headers=admin_headers,
        json={
            "title": "内部提示词大赛",
            "summary": "评选最佳内部提示词。",
            "rules_markdown": "# 规则",
            "start_at": "2998-01-01T00:00:00Z",
            "end_at": "2999-01-01T00:00:00Z",
        },
    ).json()

    created_user = client.post(
        "/api/v1/admin/users",
        headers=admin_headers,
        json={
            "username": "newcomer",
            "display_name": "Newcomer",
            "password": "password1",
            "role": "EMPLOYEE",
        },
    ).json()

    competition_create = [r for r in audit_records if getattr(r, "action", None) == "admin.competition.create"]
    assert len(competition_create) == 1
    assert competition_create[0].actor_id == admin_id
    assert competition_create[0].target_type == "competition"
    assert competition_create[0].target_id == competition["id"]

    user_create = [r for r in audit_records if getattr(r, "action", None) == "admin.user.create"]
    assert len(user_create) == 1
    assert user_create[0].target_id == created_user["id"]
    assert user_create[0].username == "newcomer"
