from __future__ import annotations

from fastapi.testclient import TestClient

from test_artifacts import login, seed_user


def create_task(client: TestClient, headers: dict[str, str], **overrides) -> dict:
    payload = {
        "title": "整理内部提示词清单",
        "description": "收集各组常用提示词并归类整理。",
        "deadline_at": None,
    }
    payload.update(overrides)
    response = client.post("/api/v1/tasks", headers=headers, json=payload)
    assert response.status_code == 201, response.text
    return response.json()


def test_task_lifecycle_and_permissions(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="creator")
    seed_user(test_settings, username="other")
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    creator_headers = login(client, "creator")

    task = create_task(client, creator_headers, deadline_at="2026-09-01T12:00:00Z")
    task_id = task["id"]
    assert task["status"] == "OPEN"
    assert task["creator"]["username"] == "creator"
    assert task["deadline_at"] == "2026-09-01T12:00:00Z"
    assert task["created_at"].endswith("Z")

    updated = client.patch(
        f"/api/v1/tasks/{task_id}",
        headers=creator_headers,
        json={"title": "整理内部提示词清单（更新）"},
    )
    assert updated.status_code == 200
    cleared = client.patch(f"/api/v1/tasks/{task_id}", headers=creator_headers, json={"deadline_at": None})
    assert cleared.status_code == 200
    assert cleared.json()["deadline_at"] is None

    other = TestClient(client.app)
    try:
        other_headers = login(other, "other")
        assert other.get(f"/api/v1/tasks/{task_id}").status_code == 200
        assert other.patch(f"/api/v1/tasks/{task_id}", headers=other_headers, json={"title": "越权修改"}).status_code == 403
        assert other.post(f"/api/v1/tasks/{task_id}/complete", headers=other_headers).status_code == 403
        assert other.post(f"/api/v1/tasks/{task_id}/close", headers=other_headers).status_code == 403
    finally:
        other.close()

    completed = client.post(f"/api/v1/tasks/{task_id}/complete", headers=creator_headers)
    assert completed.status_code == 200
    assert completed.json()["status"] == "COMPLETED"
    assert completed.json()["completed_at"] is not None
    assert completed.json()["completed_at"].endswith("Z")
    assert client.patch(
        f"/api/v1/tasks/{task_id}",
        headers=creator_headers,
        json={"title": "完成后不可编辑"},
    ).status_code == 409
    assert client.post(f"/api/v1/tasks/{task_id}/complete", headers=creator_headers).status_code == 409
    assert client.post(f"/api/v1/tasks/{task_id}/close", headers=creator_headers).status_code == 409

    admin = TestClient(client.app)
    try:
        admin_headers = login(admin, "boss")
        closable = create_task(client, creator_headers, title="将被管理员关闭的任务", description="内容不再需要。")
        closed = admin.post(f"/api/v1/tasks/{closable['id']}/close", headers=admin_headers)
        assert closed.status_code == 200
        assert closed.json()["status"] == "CLOSED"
        assert closed.json()["closed_at"] is not None
        assert client.post(f"/api/v1/tasks/{closable['id']}/close", headers=creator_headers).status_code == 409
        assert client.patch(
            f"/api/v1/tasks/{closable['id']}",
            headers=creator_headers,
            json={"title": "关闭后不可编辑"},
        ).status_code == 409
    finally:
        admin.close()

    listing = client.get("/api/v1/tasks")
    assert listing.status_code == 200
    assert listing.json()["total"] == 2
    assert client.get("/api/v1/tasks", params={"mine": True}).json()["total"] == 2
    assert client.get("/api/v1/tasks", params={"status": "OPEN"}).json()["total"] == 0
    assert client.get("/api/v1/tasks", params={"status": "COMPLETED"}).json()["total"] == 1
    assert client.get("/api/v1/tasks", params={"q": "管理员"}).json()["total"] == 1
    detail = client.get(f"/api/v1/tasks/{task_id}")
    assert detail.json()["description"]


def test_task_deadline_requires_utc_offset(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="creator")
    headers = login(client, "creator")

    naive = client.post(
        "/api/v1/tasks",
        headers=headers,
        json={"title": "无时区截止时间", "description": "正文", "deadline_at": "2026-09-01T12:00:00"},
    )
    assert naive.status_code == 422

    converted = create_task(
        client,
        headers,
        deadline_at="2026-09-01T20:00:00+08:00",
    )
    assert converted["deadline_at"] == "2026-09-01T12:00:00Z"

    assert client.patch(
        f"/api/v1/tasks/{converted['id']}",
        headers=headers,
        json={"deadline_at": "2026-09-02T09:00:00"},
    ).status_code == 422


def test_task_validation_and_not_found(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="creator")
    headers = login(client, "creator")

    assert client.post(
        "/api/v1/tasks",
        headers=headers,
        json={"title": "   ", "description": "正文"},
    ).status_code == 422
    assert client.post(
        "/api/v1/tasks",
        headers=headers,
        json={"title": "标题", "description": "正文", "extra": True},
    ).status_code == 422

    task = create_task(client, headers)
    assert client.patch(
        f"/api/v1/tasks/{task['id']}",
        headers=headers,
        json={"title": None},
    ).status_code == 422
    assert client.get("/api/v1/tasks/99999").status_code == 404
