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


def test_competition_lifecycle_status_and_permissions(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    seed_user(test_settings, username="staff")
    admin_headers = login(client, "boss")

    upcoming = create_competition(client, admin_headers)
    assert upcoming["status"] == "UPCOMING"
    assert upcoming["creator"]["username"] == "boss"
    assert upcoming["start_at"] == "2998-01-01T00:00:00Z"
    assert upcoming["created_at"].endswith("Z")

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

        listed = staff.get("/api/v1/competitions", headers=staff_headers)
        assert listed.status_code == 200
        body = listed.json()
        assert body["total"] == 3
        assert {item["status"] for item in body["items"]} == {"UPCOMING", "ONGOING", "ENDED"}
        # Ordered by start_at descending, so the furthest-start competition leads.
        assert body["items"][0]["title"] == "内部提示词大赛"

        searched = staff.get("/api/v1/competitions", params={"q": "已结束"}, headers=staff_headers)
        assert searched.status_code == 200
        assert [item["title"] for item in searched.json()["items"]] == ["已结束的竞赛"]

        detail = staff.get(f"/api/v1/competitions/{ongoing['id']}", headers=staff_headers)
        assert detail.status_code == 200
        assert detail.json()["status"] == "ONGOING"
        assert "# 规则" in detail.json()["rules_markdown"]

        assert staff.get("/api/v1/competitions/999", headers=staff_headers).status_code == 404
    finally:
        staff.close()


def test_competition_time_validation(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    admin_headers = login(client, "boss")

    for overrides in (
        {"start_at": "2999-01-02T00:00:00Z", "end_at": "2999-01-01T00:00:00Z"},
        {"start_at": "2999-01-01T00:00:00Z", "end_at": "2999-01-01T00:00:00Z"},
        {"start_at": "2999-01-01T00:00:00"},
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
