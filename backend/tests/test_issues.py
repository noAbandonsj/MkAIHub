from __future__ import annotations

from fastapi.testclient import TestClient

from test_artifacts import login, seed_user


def create_issue(client: TestClient, headers: dict[str, str], **overrides) -> dict:
    payload = {"title": "希望支持导出 Markdown", "description": "详情页增加导出按钮。"}
    payload.update(overrides)
    response = client.post("/api/v1/issues", headers=headers, json=payload)
    assert response.status_code == 201, response.text
    return response.json()


def test_issue_lifecycle_comments_and_permissions(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="author")
    seed_user(test_settings, username="other")
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    author_headers = login(client, "author")

    issue = create_issue(client, author_headers)
    issue_id = issue["id"]
    assert issue["status"] == "OPEN"
    assert issue["author"]["username"] == "author"

    updated = client.patch(
        f"/api/v1/issues/{issue_id}",
        headers=author_headers,
        json={"description": "详情页增加导出按钮，支持附件一起打包。"},
    )
    assert updated.status_code == 200

    other = TestClient(client.app)
    try:
        other_headers = login(other, "other")
        assert other.get(f"/api/v1/issues/{issue_id}").status_code == 200
        assert other.patch(f"/api/v1/issues/{issue_id}", headers=other_headers, json={"title": "越权修改"}).status_code == 403
        assert other.post(f"/api/v1/issues/{issue_id}/close", headers=other_headers).status_code == 403

        comment = other.post(
            f"/api/v1/issues/{issue_id}/comments",
            headers=other_headers,
            json={"content": "支持这个建议。"},
        )
        assert comment.status_code == 201
        assert comment.json()["issue_id"] == issue_id
        assert comment.json()["artifact_id"] is None
        assert comment.json()["created_at"].endswith("Z")
        comment_id = comment.json()["id"]

        author_comment = client.post(
            f"/api/v1/issues/{issue_id}/comments",
            headers=author_headers,
            json={"content": "感谢反馈。"},
        )
        assert author_comment.status_code == 201

        comments = other.get(f"/api/v1/issues/{issue_id}/comments")
        assert comments.status_code == 200
        assert comments.json()["total"] == 2

        assert client.delete(f"/api/v1/comments/{comment_id}", headers=author_headers).status_code == 403
        assert other.delete(f"/api/v1/comments/{comment_id}", headers=other_headers).status_code == 204
    finally:
        other.close()

    closed = client.post(f"/api/v1/issues/{issue_id}/close", headers=author_headers)
    assert closed.status_code == 200
    assert closed.json()["status"] == "CLOSED"
    assert closed.json()["closed_at"] is not None
    assert closed.json()["closed_at"].endswith("Z")
    assert client.patch(
        f"/api/v1/issues/{issue_id}",
        headers=author_headers,
        json={"title": "关闭后不可编辑"},
    ).status_code == 409
    assert client.post(
        f"/api/v1/issues/{issue_id}/comments",
        headers=author_headers,
        json={"content": "关闭后不可评论"},
    ).status_code == 409
    assert client.post(f"/api/v1/issues/{issue_id}/close", headers=author_headers).status_code == 409

    admin = TestClient(client.app)
    try:
        admin_headers = login(admin, "boss")
        reopened = admin.post(f"/api/v1/issues/{issue_id}/reopen", headers=admin_headers)
        assert reopened.status_code == 200
        assert reopened.json()["status"] == "OPEN"
        assert reopened.json()["closed_at"] is None
    finally:
        admin.close()

    assert client.post(
        f"/api/v1/issues/{issue_id}/comments",
        headers=author_headers,
        json={"content": "重新开放后可以继续讨论。"},
    ).status_code == 201
    assert client.post(f"/api/v1/issues/{issue_id}/reopen", headers=author_headers).status_code == 409

    listing = client.get("/api/v1/issues")
    assert listing.status_code == 200
    assert listing.json()["total"] == 1
    assert client.get("/api/v1/issues", params={"mine": True}).json()["total"] == 1
    assert client.get("/api/v1/issues", params={"status": "OPEN"}).json()["total"] == 1
    assert client.get("/api/v1/issues", params={"status": "CLOSED"}).json()["total"] == 0


def test_issue_validation_and_search(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="author")
    headers = login(client, "author")

    assert client.post(
        "/api/v1/issues",
        headers=headers,
        json={"title": "", "description": "正文"},
    ).status_code == 422
    assert client.post(
        "/api/v1/issues",
        headers=headers,
        json={"title": "标题", "description": "正文", "unexpected": True},
    ).status_code == 422
    assert client.get("/api/v1/issues/99999").status_code == 404

    create_issue(client, headers, title="搜索关键词甲", description="正文一")
    create_issue(client, headers, title="普通标题", description="包含关键词乙的正文")
    assert client.get("/api/v1/issues", params={"q": "关键词甲"}).json()["total"] == 1
    assert client.get("/api/v1/issues", params={"q": "关键词乙"}).json()["total"] == 1
