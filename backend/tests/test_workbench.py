from __future__ import annotations

from contextlib import ExitStack

from test_artifacts import seed_user
from test_competition_closure import add_task, create_competition, register, review
from test_task_closure import publish_artifact, user_session


def test_workbench_counts_statistics_and_cross_module_filters(client, test_settings) -> None:
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    seed_user(test_settings, username="creator")
    seed_user(test_settings, username="member")

    with ExitStack() as stack:
        boss_c, boss = user_session(stack, client.app, "boss")
        creator_c, creator = user_session(stack, client.app, "creator")
        member_c, member = user_session(stack, client.app, "member")

        independent_task = creator_c.post(
            "/api/v1/tasks",
            headers=creator,
            json={"title": "独立闭环任务", "description": "用于待验收统计。", "deadline_at": None},
        ).json()
        assert member_c.post(
            f"/api/v1/tasks/{independent_task['id']}/participants", headers=member
        ).status_code == 201
        independent_artifact = publish_artifact(member_c, member, title="独立任务成果")
        independent_submission = member_c.post(
            f"/api/v1/tasks/{independent_task['id']}/submissions",
            headers=member,
            json={"artifact_id": independent_artifact["id"]},
        ).json()

        competition = create_competition(boss_c, boss, title="批次十竞赛")
        competition_task = add_task(boss_c, boss, competition["id"], title="竞赛闭环任务")
        assert boss_c.post(
            f"/api/v1/admin/competitions/{competition['id']}/publish", headers=boss
        ).status_code == 200
        register(member_c, member, competition["id"])
        competition_artifact = publish_artifact(member_c, member, title="竞赛任务成果")
        competition_submission = member_c.post(
            f"/api/v1/tasks/{competition_task['id']}/submissions",
            headers=member,
            json={"artifact_id": competition_artifact["id"]},
        ).json()

        member_summary = member_c.get("/api/v1/workbench", headers=member)
        assert member_summary.status_code == 200, member_summary.text
        assert member_summary.json()["counts"] == {
            "participated_tasks": 2,
            "competition_tasks": 1,
            "pending_task_reviews": 0,
            "pending_competition_reviews": 0,
        }

        creator_summary = creator_c.get("/api/v1/workbench", headers=creator).json()
        assert creator_summary["counts"]["pending_task_reviews"] == 1

        boss_summary = boss_c.get("/api/v1/workbench", headers=boss).json()
        assert boss_summary["counts"]["pending_competition_reviews"] == 1
        assert boss_summary["statistics"] == {
            "participations": 2,
            "submissions": 2,
            "accepted_submissions": 0,
            "competition_task_completions": 1,
            "published_results": 0,
        }

        competition_tasks = member_c.get(
            "/api/v1/tasks",
            params={"participated": True, "competition_only": True},
            headers=member,
        ).json()
        assert competition_tasks["total"] == 1
        assert competition_tasks["items"][0]["id"] == competition_task["id"]

        pending_competition_reviews = boss_c.get(
            "/api/v1/tasks", params={"pending_competition_review": True}, headers=boss
        )
        assert pending_competition_reviews.status_code == 200
        assert pending_competition_reviews.json()["total"] == 1
        assert member_c.get(
            "/api/v1/tasks", params={"pending_competition_review": True}, headers=member
        ).status_code == 403

        task_artifacts = member_c.get(
            "/api/v1/artifacts", params={"source": "TASK_RESULT"}, headers=member
        ).json()
        assert task_artifacts["total"] == 1
        assert task_artifacts["items"][0]["id"] == independent_artifact["id"]
        assert task_artifacts["items"][0]["source_types"] == ["TASK_RESULT"]

        competition_artifacts = member_c.get(
            "/api/v1/artifacts", params={"source": "COMPETITION_ENTRY"}, headers=member
        ).json()
        assert competition_artifacts["total"] == 1
        assert competition_artifacts["items"][0]["id"] == competition_artifact["id"]
        assert competition_artifacts["items"][0]["source_types"] == ["COMPETITION_ENTRY"]

        assert creator_c.post(
            f"/api/v1/task-submissions/{independent_submission['id']}/accept",
            headers=creator,
            json={"note": "验收通过"},
        ).status_code == 200
        assert review(
            boss_c, boss, competition_submission["id"], "20.00", comment="评审完成"
        ).status_code == 200

        assert creator_c.get("/api/v1/workbench", headers=creator).json()["counts"][
            "pending_task_reviews"
        ] == 0
        refreshed_boss = boss_c.get("/api/v1/workbench", headers=boss).json()
        assert refreshed_boss["counts"]["pending_competition_reviews"] == 0
        assert refreshed_boss["statistics"]["accepted_submissions"] == 1
