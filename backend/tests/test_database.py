from __future__ import annotations

from datetime import datetime
from pathlib import Path

from alembic import command
from alembic.config import Config
from sqlalchemy import create_engine, inspect, text

from app.db.session import SQLITE_BUSY_TIMEOUT_MS, create_engine_from_url, session_factory
from tests.conftest import sqlite_url


BACKEND_ROOT = Path(__file__).resolve().parents[1]


def test_sqlite_connection_pragmas(tmp_path: Path) -> None:
    engine = create_engine_from_url(sqlite_url(tmp_path / "pragmas.db"))
    try:
        with engine.connect() as connection:
            assert connection.execute(text("PRAGMA foreign_keys")).scalar_one() == 1
            assert connection.execute(text("PRAGMA journal_mode")).scalar_one().lower() == "wal"
            assert connection.execute(text("PRAGMA busy_timeout")).scalar_one() >= SQLITE_BUSY_TIMEOUT_MS
    finally:
        engine.dispose()


def test_alembic_upgrade_from_empty_database(tmp_path: Path) -> None:
    database_url = sqlite_url(tmp_path / "migration.db")
    config = Config(str(BACKEND_ROOT / "alembic.ini"))
    config.set_main_option("sqlalchemy.url", database_url)

    command.upgrade(config, "head")

    engine = create_engine(database_url)
    try:
        table_names = set(inspect(engine).get_table_names())
        assert table_names == {
            "alembic_version",
            "artifact_files",
            "artifacts",
            "comments",
            "competition_registrations",
            "competition_results",
            "competition_reviews",
            "competitions",
            "files",
            "issues",
            "task_participants",
            "task_submissions",
            "tasks",
            "user_sessions",
            "users",
        }
        with engine.connect() as connection:
            assert connection.execute(text("SELECT version_num FROM alembic_version")).scalar_one() == (
                "20260819_0008"
            )
            assert connection.execute(text("PRAGMA foreign_key_check")).fetchall() == []
    finally:
        engine.dispose()


def test_alembic_upgrade_from_first_version_data(tmp_path: Path) -> None:
    database_url = sqlite_url(tmp_path / "migration-first-version.db")
    config = Config(str(BACKEND_ROOT / "alembic.ini"))
    config.set_main_option("sqlalchemy.url", database_url)
    command.upgrade(config, "20260819_0005")

    engine = create_engine(database_url)
    try:
        with engine.connect() as connection:
            connection.execute(
                text(
                    "INSERT INTO users (username, display_name, password_hash, role, is_active, created_at, updated_at) "
                    "VALUES ('legacy', 'Legacy', 'x', 'EMPLOYEE', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                )
            )
            user_id = connection.execute(text("SELECT id FROM users")).scalar_one()
            connection.execute(
                text(
                    "INSERT INTO competitions (title, summary, rules_markdown, start_at, end_at, created_by, created_at, updated_at) "
                    "VALUES ('首版竞赛', 's', 'r', '2020-01-01 00:00:00', '2999-01-01 00:00:00', :uid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                ),
                {"uid": user_id},
            )
            connection.execute(
                text(
                    "INSERT INTO tasks (title, description, creator_id, status, created_at, updated_at) "
                    "VALUES ('首版任务', 'd', :uid, 'OPEN', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                ),
                {"uid": user_id},
            )
            connection.execute(
                text(
                    "INSERT INTO tasks (title, description, creator_id, status, created_at, updated_at) "
                    "VALUES ('首版已完成', 'd', :uid, 'COMPLETED', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                ),
                {"uid": user_id},
            )
            connection.commit()
    finally:
        engine.dispose()

    command.upgrade(config, "20260819_0006")
    engine = create_engine(database_url)
    try:
        table_names = set(inspect(engine).get_table_names())
        assert {"task_participants", "task_submissions"} <= table_names
    finally:
        engine.dispose()

    command.upgrade(config, "head")
    engine = create_engine(database_url)
    try:
        with engine.connect() as connection:
            assert connection.execute(text("SELECT version_num FROM alembic_version")).scalar_one() == (
                "20260819_0008"
            )
            rows = connection.execute(text("SELECT title, status, competition_id FROM tasks ORDER BY id")).fetchall()
            assert [(row[0], row[1], row[2]) for row in rows] == [
                ("首版任务", "OPEN", None),
                ("首版已完成", "COMPLETED", None),
            ]
            assert connection.execute(text("SELECT title, status FROM competitions")).fetchone() == (
                "首版竞赛",
                "PUBLISHED",
            )
            assert connection.execute(text("PRAGMA foreign_key_check")).fetchall() == []
    finally:
        engine.dispose()


def test_alembic_stepwise_downgrade_to_first_version(tmp_path: Path) -> None:
    from app.models import (
        Artifact,
        Competition,
        CompetitionRegistration,
        Task,
        TaskParticipant,
        TaskSubmission,
        User,
    )

    database_url = sqlite_url(tmp_path / "migration-downgrade-first.db")
    config = Config(str(BACKEND_ROOT / "alembic.ini"))
    config.set_main_option("sqlalchemy.url", database_url)
    command.upgrade(config, "head")

    engine = create_engine_from_url(database_url)
    try:
        with session_factory(engine)() as db:
            user = User(
                username="downgrade",
                display_name="Downgrade",
                password_hash="x",
                role="SYSTEM_ADMIN",
                is_active=True,
            )
            db.add(user)
            db.flush()
            competition = Competition(
                title="降级竞赛",
                summary="s",
                rules_markdown="r",
                start_at=datetime(2020, 1, 1),
                end_at=datetime(2999, 1, 1),
                status="PUBLISHED",
                created_by=user.id,
            )
            db.add(competition)
            db.flush()
            task = Task(title="降级任务", description="d", creator_id=user.id, status="OPEN")
            db.add(task)
            db.flush()
            artifact = Artifact(
                title="降级展品",
                summary="s",
                content_markdown="# c",
                author_id=user.id,
                status="PUBLISHED",
            )
            db.add(artifact)
            db.flush()
            participant = TaskParticipant(task_id=task.id, user_id=user.id, status="ACTIVE")
            db.add(participant)
            db.flush()
            db.add(
                TaskSubmission(
                    task_id=task.id,
                    participant_id=participant.id,
                    artifact_id=artifact.id,
                    round_no=1,
                    status="SUBMITTED",
                    is_current=True,
                )
            )
            db.add(CompetitionRegistration(competition_id=competition.id, user_id=user.id, status="REGISTERED"))
            db.commit()
    finally:
        engine.dispose()

    command.downgrade(config, "20260819_0006")
    engine = create_engine(database_url)
    try:
        table_names = set(inspect(engine).get_table_names())
        assert not {"competition_registrations", "competition_reviews", "competition_results"} & table_names
        with engine.connect() as connection:
            task_columns = [row[1] for row in connection.execute(text("PRAGMA table_info(tasks)")).fetchall()]
            assert "competition_id" not in task_columns
            competition_columns = [row[1] for row in connection.execute(text("PRAGMA table_info(competitions)")).fetchall()]
            assert "status" not in competition_columns
            assert connection.execute(text("SELECT title FROM tasks")).scalar_one() == "降级任务"
            assert connection.execute(text("SELECT title FROM competitions")).scalar_one() == "降级竞赛"
            assert connection.execute(text("PRAGMA foreign_key_check")).fetchall() == []
    finally:
        engine.dispose()

    command.downgrade(config, "20260819_0005")
    engine = create_engine(database_url)
    try:
        table_names = set(inspect(engine).get_table_names())
        assert not {"task_participants", "task_submissions"} & table_names
        with engine.connect() as connection:
            assert connection.execute(text("SELECT version_num FROM alembic_version")).scalar_one() == (
                "20260819_0005"
            )
            assert connection.execute(text("SELECT title, status FROM tasks")).fetchone() == ("降级任务", "OPEN")
            assert connection.execute(text("PRAGMA foreign_key_check")).fetchall() == []
    finally:
        engine.dispose()

    command.upgrade(config, "head")
    engine = create_engine(database_url)
    try:
        with engine.connect() as connection:
            assert connection.execute(text("SELECT title, status FROM tasks")).fetchone() == ("降级任务", "OPEN")
            assert connection.execute(text("SELECT title, status FROM competitions")).fetchone() == (
                "降级竞赛",
                "PUBLISHED",
            )
            assert connection.execute(text("PRAGMA foreign_key_check")).fetchall() == []
    finally:
        engine.dispose()


def test_alembic_downgrade_and_upgrade_with_data(tmp_path: Path) -> None:
    database_url = sqlite_url(tmp_path / "migration-data.db")
    config = Config(str(BACKEND_ROOT / "alembic.ini"))
    config.set_main_option("sqlalchemy.url", database_url)
    command.upgrade(config, "head")

    engine = create_engine_from_url(database_url)
    try:
        with engine.connect() as connection:
            connection.execute(
                text(
                    "INSERT INTO users (username, display_name, password_hash, role, is_active, created_at, updated_at) "
                    "VALUES ('u1', 'U1', 'x', 'SYSTEM_ADMIN', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                )
            )
            user_id = connection.execute(text("SELECT id FROM users")).scalar_one()
            connection.execute(
                text(
                    "INSERT INTO competitions (title, summary, rules_markdown, start_at, end_at, created_by, created_at, updated_at) "
                    "VALUES ('数据赛', 's', 'r', '2020-01-01 00:00:00', '2999-01-01 00:00:00', :uid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                ),
                {"uid": user_id},
            )
            competition_id = connection.execute(text("SELECT id FROM competitions")).scalar_one()
            connection.execute(
                text(
                    "INSERT INTO tasks (title, description, creator_id, status, competition_id, competition_required, "
                    "competition_sort_order, competition_max_score, competition_weight, created_at, updated_at) "
                    "VALUES ('数据任务', 'd', :uid, 'OPEN', :cid, 1, 1, 30.00, 10.00, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                ),
                {"uid": user_id, "cid": competition_id},
            )
            task_id = connection.execute(text("SELECT id FROM tasks")).scalar_one()
            connection.execute(
                text(
                    "INSERT INTO task_participants (task_id, user_id, status, joined_at, created_at, updated_at) "
                    "VALUES (:tid, :uid, 'ACTIVE', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                ),
                {"tid": task_id, "uid": user_id},
            )
            connection.commit()
    finally:
        engine.dispose()

    command.downgrade(config, "20260819_0006")

    engine = create_engine(database_url)
    try:
        with engine.connect() as connection:
            assert "competition_id" not in [
                row[1] for row in connection.execute(text("PRAGMA table_info(tasks)")).fetchall()
            ]
            assert "status" not in [
                row[1] for row in connection.execute(text("PRAGMA table_info(competitions)")).fetchall()
            ]
            assert connection.execute(text("SELECT title FROM tasks")).scalar_one() == "数据任务"
            assert connection.execute(text("PRAGMA foreign_key_check")).fetchall() == []
    finally:
        engine.dispose()

    command.upgrade(config, "head")
    engine = create_engine(database_url)
    try:
        with engine.connect() as connection:
            assert connection.execute(text("SELECT version_num FROM alembic_version")).scalar_one() == (
                "20260819_0008"
            )
            assert connection.execute(text("SELECT title FROM tasks")).scalar_one() == "数据任务"
            assert connection.execute(text("SELECT status FROM competitions")).scalar_one() == "PUBLISHED"
            assert connection.execute(text("PRAGMA foreign_key_check")).fetchall() == []
    finally:
        engine.dispose()


def test_alembic_backfills_competition_task_participants(tmp_path: Path) -> None:
    database_url = sqlite_url(tmp_path / "migration-auto-participants.db")
    config = Config(str(BACKEND_ROOT / "alembic.ini"))
    config.set_main_option("sqlalchemy.url", database_url)
    command.upgrade(config, "20260819_0007")

    engine = create_engine(database_url)
    try:
        with engine.connect() as connection:
            connection.execute(
                text(
                    "INSERT INTO users (username, display_name, password_hash, role, is_active, created_at, updated_at) "
                    "VALUES ('registered', 'Registered', 'x', 'EMPLOYEE', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP), "
                    "('orphan', 'Orphan', 'x', 'EMPLOYEE', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                )
            )
            registered_id = connection.execute(
                text("SELECT id FROM users WHERE username = 'registered'")
            ).scalar_one()
            orphan_id = connection.execute(text("SELECT id FROM users WHERE username = 'orphan'")).scalar_one()
            connection.execute(
                text(
                    "INSERT INTO competitions "
                    "(title, summary, rules_markdown, start_at, end_at, status, created_by, created_at, updated_at) "
                    "VALUES ('自动领取赛', 's', 'r', '2020-01-01 00:00:00', '2999-01-01 00:00:00', "
                    "'PUBLISHED', :uid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                ),
                {"uid": registered_id},
            )
            competition_id = connection.execute(text("SELECT id FROM competitions")).scalar_one()
            for sort_order in (1, 2):
                connection.execute(
                    text(
                        "INSERT INTO tasks "
                        "(title, description, creator_id, status, competition_id, competition_required, "
                        "competition_sort_order, competition_max_score, competition_weight, created_at, updated_at) "
                        "VALUES (:title, 'd', :uid, 'OPEN', :cid, :required, :sort_order, 30.00, 10.00, "
                        "CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                    ),
                    {
                        "title": f"任务{sort_order}",
                        "uid": registered_id,
                        "cid": competition_id,
                        "required": sort_order == 1,
                        "sort_order": sort_order,
                    },
                )
            task_ids = connection.execute(text("SELECT id FROM tasks ORDER BY id")).scalars().all()
            connection.execute(
                text(
                    "INSERT INTO competition_registrations "
                    "(competition_id, user_id, status, registered_at, created_at, updated_at) "
                    "VALUES (:cid, :uid, 'REGISTERED', '2026-08-27 01:02:03', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                ),
                {"cid": competition_id, "uid": registered_id},
            )
            connection.execute(
                text(
                    "INSERT INTO task_participants "
                    "(task_id, user_id, status, joined_at, left_at, created_at, updated_at) "
                    "VALUES (:tid, :uid, 'LEFT', '2026-08-26 01:00:00', '2026-08-26 02:00:00', "
                    "CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                ),
                {"tid": task_ids[0], "uid": registered_id},
            )
            connection.execute(
                text(
                    "INSERT INTO task_participants "
                    "(task_id, user_id, status, joined_at, created_at, updated_at) "
                    "VALUES (:tid, :uid, 'ACTIVE', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)"
                ),
                {"tid": task_ids[0], "uid": orphan_id},
            )
            connection.commit()
    finally:
        engine.dispose()

    command.upgrade(config, "head")
    engine = create_engine(database_url)
    try:
        with engine.connect() as connection:
            rows = connection.execute(
                text(
                    "SELECT task_id, status, left_at FROM task_participants "
                    "WHERE user_id = :uid ORDER BY task_id"
                ),
                {"uid": registered_id},
            ).fetchall()
            assert [(row[0], row[1], row[2]) for row in rows] == [
                (task_ids[0], "ACTIVE", None),
                (task_ids[1], "ACTIVE", None),
            ]
            assert connection.execute(
                text("SELECT status FROM task_participants WHERE user_id = :uid"),
                {"uid": orphan_id},
            ).scalar_one() == "LEFT"
            assert connection.execute(text("SELECT version_num FROM alembic_version")).scalar_one() == (
                "20260819_0008"
            )
            assert connection.execute(text("PRAGMA foreign_key_check")).fetchall() == []
    finally:
        engine.dispose()
