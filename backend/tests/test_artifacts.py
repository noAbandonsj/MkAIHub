from __future__ import annotations

from fastapi.testclient import TestClient

from app.db.session import create_engine_from_url, session_factory
from app.models import User
from app.services.security import hash_password


def seed_user(settings, *, username: str, role: str = "EMPLOYEE") -> int:
    engine = create_engine_from_url(settings.database_url)
    try:
        with session_factory(engine)() as db:
            user = User(
                username=username,
                display_name=username.title(),
                password_hash=hash_password("password1"),
                role=role,
                is_active=True,
            )
            db.add(user)
            db.commit()
            db.refresh(user)
            return user.id
    finally:
        engine.dispose()


def login(client: TestClient, username: str) -> dict[str, str]:
    response = client.post(
        "/api/v1/auth/login",
        json={"username": username, "password": "password1"},
    )
    assert response.status_code == 200
    response = client.get("/api/v1/auth/csrf-token")
    assert response.status_code == 200
    return {"X-CSRF-Token": response.json()["csrf_token"]}


def create_draft(client: TestClient, headers: dict[str, str], **overrides):
    payload = {
        "title": "团队提示词实践",
        "summary": "一条可复用的内部 AI 实践摘要",
        "content_markdown": "# 使用方式\n\n按步骤执行。",
        "file_ids": [],
    }
    payload.update(overrides)
    response = client.post("/api/v1/artifacts", headers=headers, json=payload)
    assert response.status_code == 201, response.text
    return response.json()


def test_artifact_attachment_publish_comment_and_visibility(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="author")
    seed_user(test_settings, username="reader")
    author_headers = login(client, "author")

    rejected = client.post(
        "/api/v1/files",
        headers=author_headers,
        files={"file": ("unsafe.exe", b"not executable", "application/octet-stream")},
    )
    assert rejected.status_code == 415

    upload = client.post(
        "/api/v1/files",
        headers=author_headers,
        files={"file": ("guide.md", b"# Internal guide", "text/markdown")},
    )
    assert upload.status_code == 201, upload.text
    file_id = upload.json()["id"]

    draft = create_draft(client, author_headers, file_ids=[file_id])
    artifact_id = draft["id"]
    assert draft["status"] == "DRAFT"
    assert draft["files"][0]["original_name"] == "guide.md"
    assert client.get("/api/v1/artifacts", params={"mine": True}).json()["total"] == 1
    assert client.get("/api/v1/artifacts").json()["total"] == 0

    reader = TestClient(client.app)
    try:
        reader_headers = login(reader, "reader")
        assert reader.get(f"/api/v1/artifacts/{artifact_id}").status_code == 404
        assert reader.get(f"/api/v1/files/{file_id}/download").status_code == 404

        published = client.post(
            f"/api/v1/artifacts/{artifact_id}/publish",
            headers=author_headers,
        )
        assert published.status_code == 200
        assert published.json()["status"] == "PUBLISHED"
        assert published.json()["published_at"].endswith("Z")

        listing = reader.get("/api/v1/artifacts", params={"q": "提示词"})
        assert listing.status_code == 200
        assert listing.json()["total"] == 1
        assert reader.get(f"/api/v1/artifacts/{artifact_id}").status_code == 200
        download = reader.get(f"/api/v1/files/{file_id}/download")
        assert download.status_code == 200
        assert download.content == b"# Internal guide"

        forbidden_edit = reader.patch(
            f"/api/v1/artifacts/{artifact_id}",
            headers=reader_headers,
            json={"title": "不允许的修改"},
        )
        assert forbidden_edit.status_code == 403
        assert reader.post(
            f"/api/v1/artifacts/{artifact_id}/archive",
            headers=reader_headers,
        ).status_code == 403

        comment = reader.post(
            f"/api/v1/artifacts/{artifact_id}/comments",
            headers=reader_headers,
            json={"content": "已经成功复用。"},
        )
        assert comment.status_code == 201
        comment_id = comment.json()["id"]
        comments = client.get(f"/api/v1/artifacts/{artifact_id}/comments")
        assert comments.json()["total"] == 1
        assert client.delete(f"/api/v1/comments/{comment_id}", headers=author_headers).status_code == 403
        assert reader.delete(f"/api/v1/comments/{comment_id}", headers=reader_headers).status_code == 204
    finally:
        reader.close()

    archived = client.post(f"/api/v1/artifacts/{artifact_id}/archive", headers=author_headers)
    assert archived.status_code == 200
    assert archived.json()["status"] == "ARCHIVED"
    assert client.get("/api/v1/artifacts").json()["total"] == 0
    # Only administrators can use a status filter to browse archived content.
    reader_again = TestClient(client.app)
    try:
        reader_headers_again = login(reader_again, "reader")
        assert reader_again.get(
            "/api/v1/artifacts", params={"status": "ARCHIVED"}, headers=reader_headers_again
        ).json()["total"] == 0
    finally:
        reader_again.close()
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    admin = TestClient(client.app)
    try:
        admin_headers = login(admin, "boss")
        admin_archived = admin.get(
            "/api/v1/artifacts", params={"status": "ARCHIVED"}, headers=admin_headers
        ).json()
        assert admin_archived["total"] == 1
        assert admin_archived["items"][0]["status"] == "ARCHIVED"
        assert admin.get("/api/v1/artifacts", headers=admin_headers).json()["total"] == 0
    finally:
        admin.close()
    assert client.patch(
        f"/api/v1/artifacts/{artifact_id}",
        headers=author_headers,
        json={"title": "归档后不可编辑"},
    ).status_code == 409
    restored = client.post(f"/api/v1/artifacts/{artifact_id}/restore", headers=author_headers)
    assert restored.status_code == 200
    assert restored.json()["status"] == "PUBLISHED"


def test_file_ownership_state_conflicts_and_delete(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="first")
    seed_user(test_settings, username="second")
    first_headers = login(client, "first")

    unreferenced = client.post(
        "/api/v1/files",
        headers=first_headers,
        files={"file": ("unused.txt", b"unused", "text/plain")},
    ).json()
    assert client.delete(f"/api/v1/files/{unreferenced['id']}", headers=first_headers).status_code == 204
    assert client.get(f"/api/v1/files/{unreferenced['id']}/download").status_code == 404

    owned = client.post(
        "/api/v1/files",
        headers=first_headers,
        files={"file": ("owned.txt", b"owned", "text/plain")},
    ).json()

    second = TestClient(client.app)
    try:
        second_headers = login(second, "second")
        forbidden = second.post(
            "/api/v1/artifacts",
            headers=second_headers,
            json={
                "title": "跨用户附件",
                "summary": "不允许使用其他人的上传文件",
                "content_markdown": "正文",
                "file_ids": [owned["id"]],
            },
        )
        assert forbidden.status_code == 403
    finally:
        second.close()

    draft = create_draft(client, first_headers, file_ids=[owned["id"]])
    artifact_id = draft["id"]
    assert client.delete(f"/api/v1/files/{owned['id']}", headers=first_headers).status_code == 409
    assert client.post(f"/api/v1/artifacts/{artifact_id}/publish", headers=first_headers).status_code == 200
    assert client.post(f"/api/v1/artifacts/{artifact_id}/publish", headers=first_headers).status_code == 409
    assert client.delete(f"/api/v1/artifacts/{artifact_id}", headers=first_headers).status_code == 409


def test_explore_returns_latest_six_published_artifacts(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="author")
    headers = login(client, "author")
    artifact_ids: list[int] = []
    for index in range(7):
        draft = create_draft(
            client,
            headers,
            title=f"展品 {index}",
            summary=f"第 {index} 条展品",
        )
        artifact_ids.append(draft["id"])
        assert client.post(
            f"/api/v1/artifacts/{draft['id']}/publish",
            headers=headers,
        ).status_code == 200

    response = client.get("/api/v1/explore")
    assert response.status_code == 200
    latest = response.json()["latest_artifacts"]
    assert len(latest) == 6
    assert latest[0]["id"] == artifact_ids[-1]
    assert artifact_ids[0] not in [item["id"] for item in latest]
