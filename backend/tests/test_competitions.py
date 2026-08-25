from __future__ import annotations

from fastapi.testclient import TestClient

from test_artifacts import login, seed_user


def create_competition(client: TestClient, headers: dict[str, str], **overrides) -> dict:
    payload = {
        "title": "内部提示词大赛",
        "summary": "以季度为单位评选最佳内部提示词。",
        "rules_markdown": "# 规则\n\n每人限提交一份作品。",
        "start_at": "2998-01-01T00:00:00Z",
        "end_at": "2999-01-01T00:00:00Z",
    }
    payload.update(overrides)
    response = client.post("/api/v1/admin/competitions", headers=headers, json=payload)
    assert response.status_code == 201, response.text
    return response.json()


def add_competition_task(client: TestClient, headers: dict[str, str], competition_id: int, **overrides) -> dict:
    payload = {
        "title": "竞赛任务一",
        "description": "完成一个可复用的提示词展品。",
        "required": True,
        "sort_order": 1,
        "max_score": "30.00",
        "weight": "10.00",
    }
    payload.update(overrides)
    response = client.post(
        f"/api/v1/admin/competitions/{competition_id}/tasks", headers=headers, json=payload
    )
    assert response.status_code == 201, response.text
    return response.json()


def test_competition_lifecycle_status_and_permissions(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    seed_user(test_settings, username="staff")
    admin_headers = login(client, "boss")

    # New competitions start as admin-only drafts.
    upcoming = create_competition(client, admin_headers)
    assert upcoming["lifecycle_status"] == "DRAFT"
    assert upcoming["status"] == "UPCOMING"
    assert upcoming["creator"]["username"] == "boss"
    assert upcoming["start_at"] == "2998-01-01T00:00:00Z"
    assert upcoming["created_at"].endswith("Z")

    staff = TestClient(client.app)
    try:
        staff_headers = login(staff, "staff")

        # Employees are read-only: they browse but never maintain competitions.
        assert staff.post(
            "/api/v1/admin/competitions",
            headers=staff_headers,
            json={
                "title": "越权创建",
                "summary": "员工不能创建竞赛",
                "rules_markdown": "规则",
                "start_at": "2998-01-01T00:00:00Z",
                "end_at": "2999-01-01T00:00:00Z",
            },
        ).status_code == 403
        assert staff.patch(
            f"/api/v1/admin/competitions/{upcoming['id']}",
            headers=staff_headers,
            json={"title": "越权编辑"},
        ).status_code == 403
        assert staff.delete(f"/api/v1/admin/competitions/{upcoming['id']}", headers=staff_headers).status_code == 403

        # Draft competitions (and their tasks) stay invisible to employees.
        assert staff.get("/api/v1/competitions", headers=staff_headers).json()["total"] == 0
        assert staff.get(f"/api/v1/competitions/{upcoming['id']}", headers=staff_headers).status_code == 404

        # A draft competition needs at least one task before publishing.
        blocked = client.post(f"/api/v1/admin/competitions/{upcoming['id']}/publish", headers=admin_headers)
        assert blocked.status_code == 409
        assert blocked.json()["code"] == "COMPETITION_NO_TASKS"

        task = add_competition_task(client, admin_headers, upcoming["id"])
        assert task["status"] == "OPEN"
        assert task["competition_id"] == upcoming["id"]
        assert task["competition_title"] == upcoming["title"]
        assert staff.get(f"/api/v1/tasks/{task['id']}", headers=staff_headers).status_code == 404

        published = client.post(f"/api/v1/admin/competitions/{upcoming['id']}/publish", headers=admin_headers)
        assert published.status_code == 200
        assert published.json()["lifecycle_status"] == "PUBLISHED"
        assert staff.get(f"/api/v1/competitions/{upcoming['id']}", headers=staff_headers).status_code == 200

        ongoing = create_competition(
            client,
            admin_headers,
            title="进行中的竞赛",
            start_at="2020-01-01T00:00:00Z",
            end_at="2999-01-01T00:00:00Z",
        )
        assert ongoing["status"] == "ONGOING"
        ended = create_competition(
            client,
            admin_headers,
            title="已结束的竞赛",
            start_at="2019-01-01T00:00:00Z",
            end_at="2020-01-01T00:00:00Z",
        )
        assert ended["status"] == "ENDED"
        add_competition_task(client, admin_headers, ongoing["id"])
        add_competition_task(client, admin_headers, ended["id"])
        for competition_id in (ongoing["id"], ended["id"]):
            assert client.post(
                f"/api/v1/admin/competitions/{competition_id}/publish", headers=admin_headers
            ).status_code == 200

        listed = staff.get("/api/v1/competitions", headers=staff_headers)
        assert listed.status_code == 200
        body = listed.json()
        assert body["total"] == 3
        assert {item["status"] for item in body["items"]} == {"UPCOMING", "ONGOING", "ENDED"}
        assert {item["lifecycle_status"] for item in body["items"]} == {"PUBLISHED"}
        # Ordered by start_at descending, so the furthest-start competition leads.
        assert body["items"][0]["title"] == "内部提示词大赛"

        searched = staff.get("/api/v1/competitions", params={"q": "已结束"}, headers=staff_headers)
        assert searched.status_code == 200
        assert [item["title"] for item in searched.json()["items"]] == ["已结束的竞赛"]

        detail = staff.get(f"/api/v1/competitions/{ongoing['id']}", headers=staff_headers)
        assert detail.status_code == 200
        assert detail.json()["status"] == "ONGOING"
        assert "# 规则" in detail.json()["rules_markdown"]
        assert detail.json()["task_count"] == 1
        assert detail.json()["registration_count"] == 0
        assert detail.json()["my_registration"] is None
        assert detail.json()["results_published"] is False

        assert staff.get("/api/v1/competitions/999", headers=staff_headers).status_code == 404
    finally:
        staff.close()


def test_competition_time_validation(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    admin_headers = login(client, "boss")

    for overrides in (
        {"start_at": "2999-01-02T00:00:00Z", "end_at": "2999-01-01T00:00:00Z"},
        {"start_at": "2999-01-01T00:00:00Z", "end_at": "2999-01-01T00:00:00Z"},
        {"start_at": "2999-01-01T00:00:00Z"},
    ):
        payload = {
            "title": "非法时间窗口",
            "summary": "开始时间不得晚于结束时间",
            "rules_markdown": "规则",
            "start_at": "2998-01-01T00:00:00Z",
            "end_at": "2999-01-01T00:00:00Z",
        }
        payload.update(overrides)
        response = client.post("/api/v1/admin/competitions", headers=admin_headers, json=payload)
        assert response.status_code == 422, (overrides, response.text)

    competition = create_competition(client, admin_headers)
    competition_id = competition["id"]

    # Patching only end_at can still invert the stored window.
    inverted = client.patch(
        f"/api/v1/admin/competitions/{competition_id}",
        headers=admin_headers,
        json={"end_at": "2997-01-01T00:00:00Z"},
    )
    assert inverted.status_code == 422
    assert inverted.json()["code"] == "COMPETITION_TIME_CONFLICT"

    assert client.patch(
        f"/api/v1/admin/competitions/{competition_id}",
        headers=admin_headers,
        json={"title": None},
    ).status_code == 422

    updated = client.patch(
        f"/api/v1/admin/competitions/{competition_id}",
        headers=admin_headers,
        json={"title": "内部提示词大赛（更新）", "end_at": "2996-01-01T00:00:00Z", "start_at": "2995-01-01T00:00:00Z"},
    )
    assert updated.status_code == 200
    assert updated.json()["title"] == "内部提示词大赛（更新）"
    assert updated.json()["status"] == "UPCOMING"

    assert client.delete(f"/api/v1/admin/competitions/{competition_id}", headers=admin_headers).status_code == 204
    assert client.get(f"/api/v1/competitions/{competition_id}", headers=admin_headers).status_code == 404
