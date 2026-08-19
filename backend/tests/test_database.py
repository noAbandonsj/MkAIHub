from __future__ import annotations

from pathlib import Path

from alembic import command
from alembic.config import Config
from sqlalchemy import create_engine, inspect, text

from app.db.session import SQLITE_BUSY_TIMEOUT_MS, create_engine_from_url
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
        assert table_names == {"alembic_version", "users", "user_sessions"}
        with engine.connect() as connection:
            assert connection.execute(text("SELECT version_num FROM alembic_version")).scalar_one() == (
                "20260819_0002"
            )
    finally:
        engine.dispose()
