from __future__ import annotations

from contextlib import ExitStack
from pathlib import Path

import pytest
from alembic import command
from alembic.config import Config
from fastapi.testclient import TestClient
from sqlalchemy.exc import IntegrityError

from test_artifacts import login, seed_user
from test_task_closure import publish_artifact, user_session


def create_competition(client: TestClient, headers: dict[str, str], **overrides) -> dict:
    payload = {
        "title": "提示词马拉松",
        "summary": "验证竞赛闭环。",
        "rules_markdown": "# 规则",
        "start_at": "2020-01-01T00:00:00Z",
        "end_at": "2999-01-01T00:00:00Z",
    }
    payload.update(overrides)
    response = client.post("/api/v1/admin/competitions", headers=headers, json=payload)
    assert response.status_code == 201, response.text
    return response.json()


def add_task(client: TestClient, headers: dict[str, str], competition_id: int, **overrides) -> dict:
    payload = {
        "title": "必做任务",
        "description": "提交一个提示词展品。",
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


def register(client: TestClient, headers: dict[str, str], competition_id: int) -> dict:
    response = client.post(f"/api/v1/competitions/{competition_id}/registrations", headers=headers)
    assert response.status_code == 201, response.text
    return response.json()


def join_and_submit(
    client: TestClient,
    headers: dict[str, str],
    task_id: int,
    artifact: dict,
    note: str | None = None,
) -> dict:
    payload = {"artifact_id": artifact["id"]}
    if note is not None:
        payload["note"] = note
    submitted = client.post(f"/api/v1/tasks/{task_id}/submissions", headers=headers, json=payload)
    assert submitted.status_code == 201, submitted.text
    return submitted.json()


def review(
    client: TestClient, headers: dict[str, str], submission_id: int, raw_score: str, comment: str | None = None
) -> TestClient:
    payload = {"raw_score": raw_score}
    if comment is not None:
        payload["comment"] = comment
    return client.post(
        f"/api/v1/task-submissions/{submission_id}/competition-review",
        headers=headers,
        json=payload,
    )


def test_competition_closure_full_flow(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    seed_user(test_settings, username="staffb")
    seed_user(test_settings, username="staffc")
    with ExitStack() as stack:
        boss_c, boss = user_session(stack, client.app, "boss")
        b_c, b = user_session(stack, client.app, "staffb")
        c_c, c = user_session(stack, client.app, "staffc")

        competition = create_competition(boss_c, boss)
        competition_id = competition["id"]
        required_task = add_task(boss_c, boss, competition_id)
        optional_task = add_task(
            boss_c,
            boss,
            competition_id,
            title="选做任务",
            required=False,
            sort_order=2,
            max_score="50.00",
            weight="5.00",
        )
        assert required_task["competition_id"] == competition_id

        # Registration needs a published competition: DRAFT stays invisible
        # to employees (404) and rejects even the administrator's registration.
        assert b_c.post(
            f"/api/v1/competitions/{competition_id}/registrations", headers=b
        ).json()["code"] == "COMPETITION_NOT_FOUND"
        assert boss_c.post(
            f"/api/v1/competitions/{competition_id}/registrations", headers=boss
        ).json()["code"] == "COMPETITION_STATE_CONFLICT"

        published = boss_c.post(f"/api/v1/admin/competitions/{competition_id}/publish", headers=boss)
        assert published.status_code == 200
        assert published.json()["lifecycle_status"] == "PUBLISHED"

        registration = register(b_c, b, competition_id)
        assert registration["status"] == "REGISTERED"
        for task in (required_task, optional_task):
            detail = b_c.get(f"/api/v1/tasks/{task['id']}", headers=b).json()
            assert detail["my_participation"]["status"] == "ACTIVE"
            assert detail["participant_count"] == 1
        duplicated = b_c.post(f"/api/v1/competitions/{competition_id}/registrations", headers=b)
        assert duplicated.status_code == 409
        assert duplicated.json()["code"] == "COMPETITION_ALREADY_REGISTERED"

        # Unregistered employees can neither join nor submit competition tasks.
        c_joined = c_c.post(f"/api/v1/tasks/{required_task['id']}/participants", headers=c)
        assert c_joined.status_code == 403
        assert c_joined.json()["code"] == "COMPETITION_REGISTRATION_REQUIRED"
        c_artifact = publish_artifact(c_c, c, title="C 的未报名成果")
        blocked = c_c.post(
            f"/api/v1/tasks/{required_task['id']}/submissions",
            headers=c,
            json={"artifact_id": c_artifact["id"]},
        )
        assert blocked.status_code == 403
        assert blocked.json()["code"] == "COMPETITION_REGISTRATION_REQUIRED"

        # Competition-task participation is managed only through registration.
        assert b_c.post(
            f"/api/v1/tasks/{required_task['id']}/participants", headers=b
        ).json()["code"] == "TASK_ALREADY_PARTICIPATED"
        assert b_c.delete(
            f"/api/v1/tasks/{required_task['id']}/participants/me", headers=b
        ).json()["code"] == "COMPETITION_STATE_CONFLICT"

        # Registered participant submits, resubmits; only the last round counts.
        b_artifact = publish_artifact(b_c, b, title="B 的竞赛成果")
        first = join_and_submit(b_c, b, required_task["id"], b_artifact, note="第一轮")
        assert first["round_no"] == 1
        assert first["status"] == "SUBMITTED"
        # Competition tasks stay OPEN no matter how many rounds arrive.
        assert b_c.get(f"/api/v1/tasks/{required_task['id']}", headers=b).json()["status"] == "OPEN"

        b_artifact_v2 = publish_artifact(b_c, b, title="B 的竞赛成果（二轮）")
        second = b_c.post(
            f"/api/v1/tasks/{required_task['id']}/submissions",
            headers=b,
            json={"artifact_id": b_artifact_v2["id"], "note": "第二轮"},
        )
        assert second.status_code == 201
        assert second.json()["round_no"] == 2
        assert second.json()["is_current"] is True
        rounds = b_c.get(
            f"/api/v1/tasks/{required_task['id']}/submissions", headers=b
        ).json()["items"]
        assert len(rounds) == 2
        assert rounds[1]["is_current"] is False

        # Accept-style decisions do not apply to competition submissions.
        assert boss_c.post(
            f"/api/v1/task-submissions/{second.json()['id']}/accept", headers=boss, json={"note": "无效"}
        ).json()["code"] == "SUBMISSION_STATE_CONFLICT"
        assert boss_c.post(f"/api/v1/tasks/{required_task['id']}/complete", headers=boss).json()["code"] == (
            "TASK_STATE_CONFLICT"
        )

        # Only administrators review, within the score range, on current rounds.
        assert review(b_c, b, second.json()["id"], "10.00").status_code == 403
        assert review(boss_c, boss, second.json()["id"], "30.01").json()["code"] == "REVIEW_SCORE_INVALID"
        assert review(boss_c, boss, first["id"], "10.00").json()["code"] == "SUBMISSION_STATE_CONFLICT"

        reviewed = review(boss_c, boss, second.json()["id"], "20.00", comment="结构清晰")
        assert reviewed.status_code == 200
        assert reviewed.json()["competition_review"]["raw_score"] == "20.00"
        assert reviewed.json()["competition_review"]["reviewer"]["username"] == "boss"

        # Reviews stay invisible to participants before results are published.
        my_view = b_c.get(
            f"/api/v1/tasks/{required_task['id']}/submissions", headers=b
        ).json()["items"]
        assert all(item["competition_review"] is None for item in my_view)

        # Results cannot be published before the competition ends.
        not_ended = boss_c.post(
            f"/api/v1/admin/competitions/{competition_id}/publish-results", headers=boss
        )
        assert not_ended.json()["code"] == "COMPETITION_NOT_ENDED"
        # Shrink end_at into the past to close the window and finish.
        assert boss_c.patch(
            f"/api/v1/admin/competitions/{competition_id}",
            headers=boss,
            json={"end_at": "2026-01-01T00:00:00Z"},
        ).status_code == 200
        late = b_c.post(
            f"/api/v1/tasks/{required_task['id']}/submissions",
            headers=b,
            json={"artifact_id": b_artifact["id"], "note": "迟交"},
        )
        assert late.json()["code"] == "COMPETITION_SUBMISSION_CLOSED"
        assert c_c.post(
            f"/api/v1/competitions/{competition_id}/registrations", headers=c
        ).json()["code"] == "COMPETITION_REGISTRATION_CLOSED"

        # The optional task's current submission of the qualified participant
        # must be reviewed too; here it has no submission at all (0 points).
        results = boss_c.post(
            f"/api/v1/admin/competitions/{competition_id}/publish-results", headers=boss
        )
        assert results.status_code == 200
        assert results.json()["lifecycle_status"] == "RESULT_PUBLISHED"

        leaderboard = b_c.get(f"/api/v1/competitions/{competition_id}/results", headers=b)
        assert leaderboard.status_code == 200
        rows = leaderboard.json()["items"]
        assert len(rows) == 1
        assert rows[0]["user"]["username"] == "staffb"
        # 20/30*10 = 6.6667 weighted, optional task missing scores 0.
        assert rows[0]["total_score"] == "6.6667"
        assert rows[0]["rank"] == 1

        # Registration after result publication is a state conflict.
        assert c_c.post(
            f"/api/v1/competitions/{competition_id}/registrations", headers=c
        ).json()["code"] == "COMPETITION_STATE_CONFLICT"

        # Reviews become visible to participants after publication.
        my_view = b_c.get(
            f"/api/v1/tasks/{required_task['id']}/submissions", headers=b
        ).json()["items"]
        current_round = next(item for item in my_view if item["is_current"])
        assert current_round["competition_review"]["raw_score"] == "20.00"
        assert current_round["competition_rank"] == 1

        # The artifact side shows the competition source after publication.
        sources = b_c.get(f"/api/v1/artifacts/{b_artifact_v2['id']}/task-submissions", headers=b).json()["items"]
        assert sources[0]["task"]["competition"]["id"] == competition_id
        assert sources[0]["competition_rank"] == 1

        # Archived competitions keep their published leaderboard readable.
        archived = boss_c.post(f"/api/v1/admin/competitions/{competition_id}/archive", headers=boss)
        assert archived.status_code == 200
        assert archived.json()["lifecycle_status"] == "ARCHIVED"
        assert b_c.get(f"/api/v1/competitions/{competition_id}/results", headers=b).json()["items"]
        # Archived competitions no longer accept new reviews or edits.
        assert review(boss_c, boss, second.json()["id"], "21.00").json()["code"] == "COMPETITION_STATE_CONFLICT"
        assert boss_c.patch(
            f"/api/v1/admin/competitions/{competition_id}", headers=boss, json={"title": "改名"}
        ).json()["code"] == "COMPETITION_STATE_CONFLICT"


def test_competition_scoring_ranking_and_ties(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    for name in ("p01", "p02", "p03"):
        seed_user(test_settings, username=name)
    with ExitStack() as stack:
        boss_c, boss = user_session(stack, client.app, "boss")
        sessions = {name: user_session(stack, client.app, name) for name in ("p01", "p02", "p03")}

        competition = create_competition(boss_c, boss)
        competition_id = competition["id"]
        required = add_task(boss_c, boss, competition_id, title="必做", required=True, sort_order=1, max_score="30.00", weight="10.00")
        optional = add_task(boss_c, boss, competition_id, title="选做", required=False, sort_order=2, max_score="50.00", weight="5.00")
        boss_c.post(f"/api/v1/admin/competitions/{competition_id}/publish", headers=boss)

        submissions = {}
        for name, (user_c, user_h) in sessions.items():
            register(user_c, user_h, competition_id)
            artifact = publish_artifact(user_c, user_h, title=f"{name} 的必做成果")
            submissions[(name, "required")] = join_and_submit(user_c, user_h, required["id"], artifact)
        # p03 skips the optional task entirely (0 points); p01 and p02 submit it.
        for name in ("p01", "p02"):
            user_c, user_h = sessions[name]
            artifact = publish_artifact(user_c, user_h, title=f"{name} 的选做成果")
            submissions[(name, "optional")] = join_and_submit(user_c, user_h, optional["id"], artifact)

        # A third participant (p03-alias via a new user) missing the required
        # task entirely must not be ranked.
        seed_user(test_settings, username="p04")
        p4_c, p4_h = user_session(stack, client.app, "p04")
        register(p4_c, p4_h, competition_id)
        p4_optional = publish_artifact(p4_c, p4_h, title="p04 的选做成果")
        submissions[("p04", "optional")] = join_and_submit(p4_c, p4_h, optional["id"], p4_optional)

        for name in ("p01", "p02"):
            assert review(boss_c, boss, submissions[(name, "required")]["id"], "20.00").status_code == 200
        assert review(boss_c, boss, submissions[("p03", "required")]["id"], "15.00").status_code == 200
        assert review(boss_c, boss, submissions[("p01", "optional")]["id"], "25.00").status_code == 200
        # p02's optional review is still missing; publication is blocked once
        # the competition has ended (the end check runs before completeness).
        assert boss_c.patch(
            f"/api/v1/admin/competitions/{competition_id}", headers=boss, json={"end_at": "2026-01-01T00:00:00Z"}
        ).status_code == 200
        incomplete = boss_c.post(
            f"/api/v1/admin/competitions/{competition_id}/publish-results", headers=boss
        )
        assert incomplete.status_code == 409
        assert incomplete.json()["code"] == "COMPETITION_REVIEWS_INCOMPLETE"
        assert review(boss_c, boss, submissions[("p02", "optional")]["id"], "25.00").status_code == 200

        # p01 and p02 tie on total (6.6667 + 2.5000), p03 follows, p04 unranked.
        registration_list = boss_c.get(
            f"/api/v1/admin/competitions/{competition_id}/registrations", headers=boss
        ).json()["items"]
        registration_ids = {row["user"]["username"]: row["id"] for row in registration_list}
        published = boss_c.post(
            f"/api/v1/admin/competitions/{competition_id}/publish-results",
            headers=boss,
            json={
                "awards": [
                    {"registration_id": registration_ids["p01"], "award": "一等奖"},
                    {"registration_id": registration_ids["p02"], "award": "一等奖"},
                ]
            },
        )
        assert published.status_code == 200, published.text

        rows = boss_c.get(f"/api/v1/competitions/{competition_id}/results", headers=boss).json()["items"]
        ranked = [(row["user"]["username"], row["total_score"], row["rank"], row["award"]) for row in rows]
        assert ranked == [
            ("p01", "9.1667", 1, "一等奖"),
            ("p02", "9.1667", 1, "一等奖"),
            ("p03", "5.0000", 3, None),
        ]
        assert all(row["user"]["username"] != "p04" for row in rows)

        # Awards must map to ranked registrations exactly once.
        invalid_award = boss_c.post(
            f"/api/v1/admin/competitions/{competition_id}/publish-results",
            headers=boss,
            json={"awards": [{"registration_id": registration_ids["p04"], "award": "参与奖"}]},
        )
        assert invalid_award.json()["code"] == "COMPETITION_AWARD_INVALID"

        # Republishing replaces the snapshot as a whole.
        republished = boss_c.post(
            f"/api/v1/admin/competitions/{competition_id}/publish-results",
            headers=boss,
            json={"awards": [{"registration_id": registration_ids["p03"], "award": "三等奖"}]},
        )
        assert republished.status_code == 200
        rows = boss_c.get(f"/api/v1/competitions/{competition_id}/results", headers=boss).json()["items"]
        awards = {row["user"]["username"]: row["award"] for row in rows}
        assert awards == {"p01": None, "p02": None, "p03": "三等奖"}


def test_registration_cancel_and_config_lock(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    seed_user(test_settings, username="staffb")
    with ExitStack() as stack:
        boss_c, boss = user_session(stack, client.app, "boss")
        b_c, b = user_session(stack, client.app, "staffb")

        competition = create_competition(boss_c, boss)
        competition_id = competition["id"]
        task = add_task(boss_c, boss, competition_id)
        boss_c.post(f"/api/v1/admin/competitions/{competition_id}/publish", headers=boss)

        # Cancel without a registration is a 404.
        assert b_c.delete(
            f"/api/v1/competitions/{competition_id}/registrations/me", headers=b
        ).json()["code"] == "REGISTRATION_NOT_FOUND"

        register(b_c, b, competition_id)
        active = b_c.get(f"/api/v1/tasks/{task['id']}", headers=b).json()["my_participation"]
        assert active["status"] == "ACTIVE"
        cancelled = b_c.delete(
            f"/api/v1/competitions/{competition_id}/registrations/me", headers=b
        )
        assert cancelled.status_code == 200
        assert cancelled.json()["status"] == "CANCELLED"
        assert cancelled.json()["cancelled_at"] is not None
        assert b_c.get(f"/api/v1/competitions/{competition_id}", headers=b).json()["registration_count"] == 0
        left = b_c.get(f"/api/v1/tasks/{task['id']}", headers=b).json()["my_participation"]
        assert left["status"] == "LEFT"

        # Re-registering reuses both registration and participant rows.
        again = register(b_c, b, competition_id)
        assert again["status"] == "REGISTERED"
        reactivated = b_c.get(f"/api/v1/tasks/{task['id']}", headers=b).json()["my_participation"]
        assert reactivated["id"] == left["id"]
        assert reactivated["status"] == "ACTIVE"

        # Submissions block cancellation.
        artifact = publish_artifact(b_c, b, title="锁定配置的成果")
        join_and_submit(b_c, b, task["id"], artifact)
        blocked = b_c.delete(
            f"/api/v1/competitions/{competition_id}/registrations/me", headers=b
        )
        assert blocked.json()["code"] == "COMPETITION_SUBMISSION_EXISTS"

        # Once submissions exist, scoring configuration freezes.
        locked = boss_c.patch(
            f"/api/v1/admin/competitions/{competition_id}/tasks/{task['id']}",
            headers=boss,
            json={"max_score": "99.00"},
        )
        assert locked.json()["code"] == "COMPETITION_CONFIG_LOCKED"
        sort_only = boss_c.patch(
            f"/api/v1/admin/competitions/{competition_id}/tasks/{task['id']}",
            headers=boss,
            json={"sort_order": 5},
        )
        assert sort_only.status_code == 200
        # Configuration values are exposed through the competition task list.
        listed_tasks = boss_c.get(
            f"/api/v1/competitions/{competition_id}/tasks", headers=boss
        ).json()["items"]
        assert listed_tasks[0]["sort_order"] == 5
        assert listed_tasks[0]["max_score"] == "30.00"
        assert listed_tasks[0]["current_submission_count"] == 1
        assert listed_tasks[0]["reviewed_count"] == 0
        assert boss_c.delete(
            f"/api/v1/admin/competitions/{competition_id}/tasks/{task['id']}", headers=boss
        ).json()["code"] == "COMPETITION_CONFIG_LOCKED"

        # Competitions with references refuse physical deletion.
        assert boss_c.delete(
            f"/api/v1/admin/competitions/{competition_id}", headers=boss
        ).json()["code"] == "COMPETITION_HAS_REFERENCES"

        # A fresh draft allows full configuration and deletion.
        draft = create_competition(boss_c, boss, title="草稿赛")
        draft_task = add_task(boss_c, boss, draft["id"])
        patched = boss_c.patch(
            f"/api/v1/admin/competitions/{draft['id']}/tasks/{draft_task['id']}",
            headers=boss,
            json={"title": "改名任务", "weight": "8.00", "deadline_at": "2998-06-01T00:00:00Z"},
        )
        assert patched.status_code == 200
        draft_tasks = boss_c.get(f"/api/v1/competitions/{draft['id']}/tasks", headers=boss).json()["items"]
        assert draft_tasks[0]["weight"] == "8.00"
        assert draft_tasks[0]["title"] == "改名任务"
        late_deadline = boss_c.patch(
            f"/api/v1/admin/competitions/{draft['id']}/tasks/{draft_task['id']}",
            headers=boss,
            json={"deadline_at": "2999-06-01T00:00:00Z"},
        )
        assert late_deadline.json()["code"] == "COMPETITION_TIME_CONFLICT"
        assert boss_c.delete(
            f"/api/v1/admin/competitions/{draft['id']}/tasks/{draft_task['id']}", headers=boss
        ).status_code == 204
        assert boss_c.delete(f"/api/v1/admin/competitions/{draft['id']}", headers=boss).status_code == 204


def test_competition_task_disable_and_reopen(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    seed_user(test_settings, username="staffb")
    with ExitStack() as stack:
        boss_c, boss = user_session(stack, client.app, "boss")
        b_c, b = user_session(stack, client.app, "staffb")

        competition = create_competition(boss_c, boss)
        competition_id = competition["id"]
        task = add_task(boss_c, boss, competition_id)
        boss_c.post(f"/api/v1/admin/competitions/{competition_id}/publish", headers=boss)
        register(b_c, b, competition_id)
        assert b_c.get(f"/api/v1/tasks/{task['id']}", headers=b).json()["my_participation"]["status"] == (
            "ACTIVE"
        )

        closed = boss_c.post(f"/api/v1/tasks/{task['id']}/close", headers=boss)
        assert closed.status_code == 200
        assert closed.json()["status"] == "CLOSED"

        artifact = publish_artifact(b_c, b, title="停用后的成果")
        disabled = b_c.post(
            f"/api/v1/tasks/{task['id']}/submissions", headers=b, json={"artifact_id": artifact["id"]}
        )
        assert disabled.json()["code"] == "COMPETITION_STATE_CONFLICT"

        reopened = boss_c.post(f"/api/v1/admin/tasks/{task['id']}/reopen", headers=boss)
        assert reopened.status_code == 200
        assert reopened.json()["status"] == "OPEN"

        resubmitted = b_c.post(
            f"/api/v1/tasks/{task['id']}/submissions", headers=b, json={"artifact_id": artifact["id"]}
        )
        assert resubmitted.status_code == 201

        # Competition task participation cannot be changed independently.
        assert b_c.delete(f"/api/v1/tasks/{task['id']}/participants/me", headers=b).json()["code"] == (
            "COMPETITION_STATE_CONFLICT"
        )


def test_competition_constraints(test_settings) -> None:
    import datetime as dt
    from decimal import Decimal

    from sqlalchemy import select

    from app.db.session import create_engine_from_url, session_factory
    from app.models import (
        Competition,
        CompetitionRegistration,
        CompetitionResult,
        CompetitionReview,
        Task,
        TaskSubmission,
        User,
    )
    from app.services.security import hash_password

    config = Config(str(Path(__file__).resolve().parents[1] / "alembic.ini"))
    config.set_main_option("sqlalchemy.url", test_settings.database_url)
    command.upgrade(config, "head")

    engine = create_engine_from_url(test_settings.database_url)
    try:
        with session_factory(engine)() as db:
            user = User(
                username="constraint",
                display_name="Constraint",
                password_hash=hash_password("password1"),
                role="SYSTEM_ADMIN",
                is_active=True,
            )
            db.add(user)
            db.flush()
            competition = Competition(
                title="约束赛",
                summary="s",
                rules_markdown="r",
                start_at=dt.datetime(2020, 1, 1),
                end_at=dt.datetime(2999, 1, 1),
                status="PUBLISHED",
                created_by=user.id,
            )
            db.add(competition)
            db.flush()
            task = Task(
                title="约束任务",
                description="d",
                creator_id=user.id,
                status="OPEN",
                competition_id=competition.id,
                competition_required=True,
                competition_sort_order=1,
                competition_max_score=Decimal("30.00"),
                competition_weight=Decimal("10.00"),
            )
            db.add(task)
            db.flush()
            from app.models import TaskParticipant
            from app.models.artifact import Artifact

            artifact = Artifact(
                title="约束展品",
                summary="s",
                content_markdown="# c",
                author_id=user.id,
                status="PUBLISHED",
            )
            db.add(artifact)
            db.flush()
            participant = TaskParticipant(task_id=task.id, user_id=user.id, status="ACTIVE")
            db.add(participant)
            db.commit()

            registration = CompetitionRegistration(
                competition_id=competition.id, user_id=user.id, status="REGISTERED"
            )
            db.add(registration)
            db.commit()

            # Duplicate registration and standalone tasks with competition columns.
            duplicate = CompetitionRegistration(
                competition_id=competition.id, user_id=user.id, status="CANCELLED"
            )
            db.add(duplicate)
            with pytest.raises(IntegrityError):
                db.flush()
            db.rollback()

            invalid_task = Task(
                title="独立任务带竞赛列",
                description="d",
                creator_id=user.id,
                status="OPEN",
                competition_max_score=Decimal("10.00"),
            )
            db.add(invalid_task)
            with pytest.raises(IntegrityError):
                db.flush()
            db.rollback()

            submission = TaskSubmission(
                task_id=task.id,
                participant_id=participant.id,
                artifact_id=artifact.id,
                round_no=1,
                status="SUBMITTED",
                is_current=True,
            )
            db.add(submission)
            db.commit()

            # One review per submission (unique) and RESTRICT on referenced rows.
            db.add(
                CompetitionReview(
                    task_submission_id=submission.id,
                    reviewer_id=user.id,
                    raw_score=Decimal("20.00"),
                )
            )
            db.commit()
            db.add(
                CompetitionReview(
                    task_submission_id=submission.id,
                    reviewer_id=user.id,
                    raw_score=Decimal("21.00"),
                )
            )
            with pytest.raises(IntegrityError):
                db.flush()
            db.rollback()

            # RESTRICT keeps reviewed submissions intact.
            referenced_submission = db.scalar(select(TaskSubmission).where(TaskSubmission.id == submission.id))
            db.delete(referenced_submission)
            with pytest.raises(IntegrityError):
                db.flush()
            db.rollback()

            db.add(
                CompetitionResult(
                    competition_id=competition.id,
                    registration_id=registration.id,
                    total_score=Decimal("6.6667"),
                    rank=1,
                    published_by=user.id,
                )
            )
            db.commit()
            # RESTRICT keeps published results' registrations intact.
            referenced_registration = db.scalar(
                select(CompetitionRegistration).where(CompetitionRegistration.id == registration.id)
            )
            db.delete(referenced_registration)
            with pytest.raises(IntegrityError):
                db.flush()
            db.rollback()

            # RESTRICT keeps registered users from being deleted.
            registered_user = db.scalar(select(User).where(User.id == user.id))
            db.delete(registered_user)
            with pytest.raises(IntegrityError):
                db.flush()
            db.rollback()

            referenced_competition = db.scalar(select(Competition).where(Competition.id == competition.id))
            db.delete(referenced_competition)
            with pytest.raises(IntegrityError):
                db.flush()
            db.rollback()
    finally:
        engine.dispose()
