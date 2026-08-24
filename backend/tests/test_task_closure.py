from __future__ import annotations

from contextlib import ExitStack
from pathlib import Path

import pytest
from alembic import command
from alembic.config import Config
from fastapi.testclient import TestClient
from sqlalchemy.exc import IntegrityError

from test_artifacts import create_draft, login, seed_user


def publish_artifact(client: TestClient, headers: dict[str, str], **overrides) -> dict:
    draft = create_draft(client, headers, **overrides)
    response = client.post(f"/api/v1/artifacts/{draft['id']}/publish", headers=headers)
    assert response.status_code == 200, response.text
    return response.json()


def user_session(stack: ExitStack, app, username: str) -> tuple[TestClient, dict[str, str]]:
    """One TestClient per user so session cookies never overwrite each other."""

    session_client = stack.enter_context(TestClient(app))
    return session_client, login(session_client, username)


def test_task_closure_full_flow(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="creator")
    seed_user(test_settings, username="memberb")
    seed_user(test_settings, username="memberc")
    seed_user(test_settings, username="outsider")
    with ExitStack() as stack:
        creator_c, creator = user_session(stack, client.app, "creator")
        memberb_c, memberb = user_session(stack, client.app, "memberb")
        memberc_c, memberc = user_session(stack, client.app, "memberc")
        outsider_c, outsider = user_session(stack, client.app, "outsider")

        created = creator_c.post(
            "/api/v1/tasks",
            headers=creator,
            json={"title": "闭环任务", "description": "完成参与、提交与验收闭环。", "deadline_at": None},
        )
        assert created.status_code == 201, created.text
        task = created.json()
        task_id = task["id"]
        assert task["status"] == "OPEN"
        assert task["my_participation"] is None
        assert task["participant_count"] == 0

        # Outsiders cannot submit before participating.
        draft = create_draft(outsider_c, outsider)
        rejected = outsider_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=outsider,
            json={"artifact_id": draft["id"], "note": "越权提交"},
        )
        assert rejected.status_code == 403
        assert rejected.json()["code"] == "FORBIDDEN"

        joined = memberb_c.post(f"/api/v1/tasks/{task_id}/participants", headers=memberb)
        assert joined.status_code == 201, joined.text
        assert joined.json()["status"] == "ACTIVE"
        assert joined.json()["user"]["username"] == "memberb"
        duplicated = memberb_c.post(f"/api/v1/tasks/{task_id}/participants", headers=memberb)
        assert duplicated.status_code == 409
        assert duplicated.json()["code"] == "TASK_ALREADY_PARTICIPATED"

        detail = creator_c.get(f"/api/v1/tasks/{task_id}").json()
        assert detail["status"] == "IN_PROGRESS"
        assert detail["participant_count"] == 1
        assert detail["my_participation"] is None
        b_detail = memberb_c.get(f"/api/v1/tasks/{task_id}").json()
        assert b_detail["my_participation"]["status"] == "ACTIVE"

        assert memberc_c.post(f"/api/v1/tasks/{task_id}/participants", headers=memberc).status_code == 201

        # Draft artifacts and other users' artifacts cannot be submitted.
        b_draft = create_draft(memberb_c, memberb, title="未发布成果")
        assert memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": b_draft["id"]},
        ).status_code == 422
        c_artifact = publish_artifact(memberc_c, memberc, title="成员C的成果")
        assert memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": c_artifact["id"]},
        ).status_code == 422
        assert memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": 99999},
        ).status_code == 404

        b_artifact = publish_artifact(memberb_c, memberb, title="成员B的成果")
        submitted = memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": b_artifact["id"], "note": "第一轮提交"},
        )
        assert submitted.status_code == 201, submitted.text
        submission = submitted.json()
        assert submission["status"] == "SUBMITTED"
        assert submission["round_no"] == 1
        assert submission["is_current"] is True
        assert submission["artifact"]["id"] == b_artifact["id"]
        assert submission["participant"]["username"] == "memberb"
        assert creator_c.get(f"/api/v1/tasks/{task_id}").json()["status"] == "REVIEWING"

        pending_again = memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": b_artifact["id"]},
        )
        assert pending_again.status_code == 409
        assert pending_again.json()["code"] == "SUBMISSION_ALREADY_PENDING"

        # Only the creator (or an administrator) may decide submissions.
        assert memberc_c.post(
            f"/api/v1/task-submissions/{submission['id']}/request-revision",
            headers=memberc,
            json={"note": "越权退回"},
        ).status_code == 403

        revision = creator_c.post(
            f"/api/v1/task-submissions/{submission['id']}/request-revision",
            headers=creator,
            json={"note": "请补充适用范围说明"},
        )
        assert revision.status_code == 200
        assert revision.json()["status"] == "REVISION_REQUIRED"
        assert revision.json()["revision_requested_at"] is not None
        assert revision.json()["decision_note"] == "请补充适用范围说明"

        # After a revision request the participant can submit a new round.
        b_artifact_v2 = publish_artifact(memberb_c, memberb, title="成员B的成果（修订）")
        resubmitted = memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": b_artifact_v2["id"], "note": "第二轮提交"},
        )
        assert resubmitted.status_code == 201
        round_two = resubmitted.json()
        assert round_two["round_no"] == 2
        assert round_two["is_current"] is True
        history = memberb_c.get(f"/api/v1/tasks/{task_id}/submissions", headers=memberb).json()["items"]
        assert len(history) == 2
        assert history[-1]["is_current"] is False

        c_submitted = memberc_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberc,
            json={"artifact_id": c_artifact["id"], "note": "C 的提交"},
        )
        assert c_submitted.status_code == 201
        c_submission = c_submitted.json()

        # Deciding a historical (non-current) round is rejected.
        assert creator_c.post(
            f"/api/v1/task-submissions/{submission['id']}/accept",
            headers=creator,
            json={"note": "验收旧轮次"},
        ).status_code == 409

        missing_note = creator_c.post(
            f"/api/v1/task-submissions/{c_submission['id']}/reject",
            headers=creator,
            json={},
        )
        assert missing_note.status_code == 422

        rejected_c = creator_c.post(
            f"/api/v1/task-submissions/{c_submission['id']}/reject",
            headers=creator,
            json={"note": "本次不采用"},
        )
        assert rejected_c.status_code == 200
        assert rejected_c.json()["status"] == "REJECTED"
        assert rejected_c.json()["decided_at"] is not None

        accepted = creator_c.post(
            f"/api/v1/task-submissions/{round_two['id']}/accept",
            headers=creator,
            json={"note": "验收通过"},
        )
        assert accepted.status_code == 200
        assert accepted.json()["status"] == "ACCEPTED"
        assert accepted.json()["decider"]["username"] == "creator"

        # All rounds decided: the derived task status falls back to IN_PROGRESS.
        assert creator_c.get(f"/api/v1/tasks/{task_id}").json()["status"] == "IN_PROGRESS"

        completed = creator_c.post(f"/api/v1/tasks/{task_id}/complete", headers=creator)
        assert completed.status_code == 200
        assert completed.json()["status"] == "COMPLETED"
        assert completed.json()["completed_at"] is not None

        assert creator_c.post(f"/api/v1/tasks/{task_id}/complete", headers=creator).status_code == 409
        assert outsider_c.post(f"/api/v1/tasks/{task_id}/participants", headers=outsider).status_code == 409
        assert memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": b_artifact["id"]},
        ).status_code == 409
        assert memberb_c.delete(f"/api/v1/tasks/{task_id}/participants/me", headers=memberb).status_code == 409


def test_submission_visibility_and_filters(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="creator")
    seed_user(test_settings, username="memberb")
    seed_user(test_settings, username="outsider")
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    with ExitStack() as stack:
        creator_c, creator = user_session(stack, client.app, "creator")
        memberb_c, memberb = user_session(stack, client.app, "memberb")
        outsider_c, outsider = user_session(stack, client.app, "outsider")
        boss_c, boss = user_session(stack, client.app, "boss")

        task_id = creator_c.post(
            "/api/v1/tasks",
            headers=creator,
            json={"title": "可见性任务", "description": "验证提交可见性与筛选。", "deadline_at": None},
        ).json()["id"]
        memberb_c.post(f"/api/v1/tasks/{task_id}/participants", headers=memberb)
        artifact = publish_artifact(memberb_c, memberb, title="B 的可见性成果")
        submission_id = memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": artifact["id"]},
        ).json()["id"]

        # Before completion only the participant, creator, and admin see rounds.
        anonymous_c = stack.enter_context(TestClient(client.app))
        assert anonymous_c.get(f"/api/v1/tasks/{task_id}/submissions").status_code == 401
        assert outsider_c.get(f"/api/v1/tasks/{task_id}/submissions", headers=outsider).json()["total"] == 0
        assert memberb_c.get(f"/api/v1/tasks/{task_id}/submissions", headers=memberb).json()["total"] == 1
        assert creator_c.get(f"/api/v1/tasks/{task_id}/submissions", headers=creator).json()["total"] == 1
        assert boss_c.get(f"/api/v1/tasks/{task_id}/submissions", headers=boss).json()["total"] == 1

        # Filters.
        assert creator_c.get("/api/v1/tasks", params={"participated": True}).json()["total"] == 0
        assert memberb_c.get("/api/v1/tasks", params={"participated": True}, headers=memberb).json()["total"] == 1
        assert creator_c.get("/api/v1/tasks", params={"pending_review": True}, headers=creator).json()["total"] == 1
        assert memberb_c.get("/api/v1/tasks", params={"pending_review": True}, headers=memberb).json()["total"] == 0

        accepted = creator_c.post(f"/api/v1/task-submissions/{submission_id}/accept", headers=creator, json={"note": "通过"})
        assert accepted.status_code == 200
        assert creator_c.post(f"/api/v1/tasks/{task_id}/complete", headers=creator).status_code == 200

        # After completion everyone sees the accepted final result.
        outsider_items = outsider_c.get(f"/api/v1/tasks/{task_id}/submissions", headers=outsider).json()["items"]
        assert len(outsider_items) == 1
        assert outsider_items[0]["status"] == "ACCEPTED"


def test_participant_leave_and_rejoin(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="creator")
    seed_user(test_settings, username="memberb")
    with ExitStack() as stack:
        creator_c, creator = user_session(stack, client.app, "creator")
        memberb_c, memberb = user_session(stack, client.app, "memberb")

        task_id = creator_c.post(
            "/api/v1/tasks",
            headers=creator,
            json={"title": "退出任务", "description": "验证退出与重新参与。", "deadline_at": None},
        ).json()["id"]

        assert memberb_c.delete(f"/api/v1/tasks/{task_id}/participants/me", headers=memberb).status_code == 404
        assert memberb_c.post(f"/api/v1/tasks/{task_id}/participants", headers=memberb).status_code == 201
        assert creator_c.get(f"/api/v1/tasks/{task_id}").json()["status"] == "IN_PROGRESS"

        left = memberb_c.delete(f"/api/v1/tasks/{task_id}/participants/me", headers=memberb)
        assert left.status_code == 200
        assert left.json()["status"] == "LEFT"
        assert left.json()["left_at"] is not None
        assert creator_c.get(f"/api/v1/tasks/{task_id}").json()["status"] == "OPEN"

        rejoined = memberb_c.post(f"/api/v1/tasks/{task_id}/participants", headers=memberb)
        assert rejoined.status_code == 201
        assert rejoined.json()["status"] == "ACTIVE"

        artifact = publish_artifact(memberb_c, memberb, title="退出前成果")
        assert memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": artifact["id"]},
        ).status_code == 201
        blocked = memberb_c.delete(f"/api/v1/tasks/{task_id}/participants/me", headers=memberb)
        assert blocked.status_code == 409
        assert blocked.json()["code"] == "TASK_SUBMISSION_EXISTS"

        participants = creator_c.get(f"/api/v1/tasks/{task_id}/participants").json()["items"]
        assert [item["user"]["username"] for item in participants] == ["memberb"]


def test_task_close_reopen_and_admin_decisions(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="creator")
    seed_user(test_settings, username="memberb")
    seed_user(test_settings, username="boss", role="SYSTEM_ADMIN")
    with ExitStack() as stack:
        creator_c, creator = user_session(stack, client.app, "creator")
        memberb_c, memberb = user_session(stack, client.app, "memberb")
        boss_c, boss = user_session(stack, client.app, "boss")

        task_id = creator_c.post(
            "/api/v1/tasks",
            headers=creator,
            json={"title": "重开任务", "description": "验证关闭与重开。", "deadline_at": None},
        ).json()["id"]
        memberb_c.post(f"/api/v1/tasks/{task_id}/participants", headers=memberb)

        closed = creator_c.post(f"/api/v1/tasks/{task_id}/close", headers=creator)
        assert closed.status_code == 200
        assert closed.json()["status"] == "CLOSED"

        # Only administrators can reopen a closed task.
        assert creator_c.post(f"/api/v1/admin/tasks/{task_id}/reopen", headers=creator).status_code == 403
        reopened = boss_c.post(f"/api/v1/admin/tasks/{task_id}/reopen", headers=boss)
        assert reopened.status_code == 200
        assert reopened.json()["status"] == "IN_PROGRESS"
        assert reopened.json()["closed_at"] is None

        # A task without participants reopens to OPEN.
        empty_id = creator_c.post(
            "/api/v1/tasks",
            headers=creator,
            json={"title": "空任务", "description": "没有参与人。", "deadline_at": None},
        ).json()["id"]
        assert boss_c.post(f"/api/v1/tasks/{empty_id}/close", headers=boss).status_code == 200
        assert boss_c.post(f"/api/v1/admin/tasks/{empty_id}/reopen", headers=boss).json()["status"] == "OPEN"
        assert boss_c.post(f"/api/v1/admin/tasks/{empty_id}/reopen", headers=boss).status_code == 409

        # Administrators may decide submissions as exception handlers.
        artifact = publish_artifact(memberb_c, memberb, title="管理员验收成果")
        submission = memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": artifact["id"]},
        ).json()
        admin_accepted = boss_c.post(
            f"/api/v1/task-submissions/{submission['id']}/accept",
            headers=boss,
            json={"note": "管理员代为验收"},
        )
        assert admin_accepted.status_code == 200
        assert creator_c.post(f"/api/v1/tasks/{task_id}/complete", headers=creator).status_code == 200
        assert boss_c.post(f"/api/v1/admin/tasks/{task_id}/reopen", headers=boss).status_code == 409

        unknown = boss_c.post("/api/v1/task-submissions/99999/accept", headers=boss, json={"note": "不存在"})
        assert unknown.status_code == 404
        assert unknown.json()["code"] == "SUBMISSION_NOT_FOUND"


def test_artifact_task_sources(client: TestClient, test_settings) -> None:
    seed_user(test_settings, username="creator")
    seed_user(test_settings, username="memberb")
    seed_user(test_settings, username="memberc")
    seed_user(test_settings, username="outsider")
    with ExitStack() as stack:
        creator_c, creator = user_session(stack, client.app, "creator")
        memberb_c, memberb = user_session(stack, client.app, "memberb")
        memberc_c, memberc = user_session(stack, client.app, "memberc")
        outsider_c, outsider = user_session(stack, client.app, "outsider")

        task_id = creator_c.post(
            "/api/v1/tasks",
            headers=creator,
            json={"title": "来源任务", "description": "验证展品反向来源。", "deadline_at": None},
        ).json()["id"]
        memberb_c.post(f"/api/v1/tasks/{task_id}/participants", headers=memberb)
        memberc_c.post(f"/api/v1/tasks/{task_id}/participants", headers=memberc)
        b_artifact = publish_artifact(memberb_c, memberb, title="B 的来源成果")
        c_artifact = publish_artifact(memberc_c, memberc, title="C 的待审成果")
        b_submission = memberb_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberb,
            json={"artifact_id": b_artifact["id"]},
        ).json()
        memberc_c.post(
            f"/api/v1/tasks/{task_id}/submissions",
            headers=memberc,
            json={"artifact_id": c_artifact["id"]},
        )

        # Pending rounds stay private; the submitter always sees their own rounds.
        assert outsider_c.get(f"/api/v1/artifacts/{c_artifact['id']}/task-submissions", headers=outsider).json()["items"] == []
        own = memberc_c.get(f"/api/v1/artifacts/{c_artifact['id']}/task-submissions", headers=memberc).json()["items"]
        assert len(own) == 1
        assert own[0]["task"]["id"] == task_id
        assert own[0]["task"]["status"] == "REVIEWING"

        accepted = creator_c.post(
            f"/api/v1/task-submissions/{b_submission['id']}/accept",
            headers=creator,
            json={"note": "采用"},
        )
        assert accepted.status_code == 200
        accepted_view = outsider_c.get(
            f"/api/v1/artifacts/{b_artifact['id']}/task-submissions", headers=outsider
        ).json()["items"]
        assert len(accepted_view) == 1
        assert accepted_view[0]["status"] == "ACCEPTED"
        assert accepted_view[0]["participant"]["username"] == "memberb"

        # The still-pending round on the other artifact stays invisible.
        assert outsider_c.get(f"/api/v1/artifacts/{c_artifact['id']}/task-submissions", headers=outsider).json()["items"] == []
        assert outsider_c.get("/api/v1/artifacts/99999/task-submissions", headers=outsider).status_code == 404


def test_closure_unique_and_foreign_key_constraints(test_settings) -> None:
    from sqlalchemy import select

    from app.db.session import create_engine_from_url, session_factory
    from app.models import Artifact, Task, TaskParticipant, TaskSubmission, User
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
                role="EMPLOYEE",
                is_active=True,
            )
            db.add(user)
            db.flush()
            task = Task(title="约束任务", description="验证唯一与外键约束。", creator_id=user.id, status="IN_PROGRESS")
            db.add(task)
            artifact = Artifact(
                title="约束展品",
                summary="用于外键约束的展品。",
                content_markdown="# 内容",
                author_id=user.id,
                status="PUBLISHED",
            )
            db.add(artifact)
            db.flush()
            participant = TaskParticipant(task_id=task.id, user_id=user.id, status="ACTIVE")
            db.add(participant)
            db.commit()
            task_id, user_id, participant_id, artifact_id = task.id, user.id, participant.id, artifact.id

            duplicate = TaskParticipant(task_id=task_id, user_id=user_id, status="ACTIVE")
            db.add(duplicate)
            with pytest.raises(IntegrityError):
                db.flush()
            db.rollback()

            round_one = TaskSubmission(
                task_id=task_id,
                participant_id=participant_id,
                artifact_id=artifact_id,
                round_no=1,
                status="SUBMITTED",
                is_current=True,
            )
            db.add(round_one)
            db.commit()
            duplicate_round = TaskSubmission(
                task_id=task_id,
                participant_id=participant_id,
                artifact_id=artifact_id,
                round_no=1,
                status="SUBMITTED",
                is_current=False,
            )
            db.add(duplicate_round)
            with pytest.raises(IntegrityError):
                db.flush()
            db.rollback()

            # RESTRICT keeps referenced tasks from being deleted.
            referenced = db.scalar(select(Task).where(Task.id == task_id))
            db.delete(referenced)
            with pytest.raises(IntegrityError):
                db.flush()
            db.rollback()
    finally:
        engine.dispose()
