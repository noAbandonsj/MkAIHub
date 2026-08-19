from __future__ import annotations

from pathlib import Path

from alembic import command
from alembic.config import Config
from sqlalchemy import select

from app.cli import main
from app.db.session import create_engine_from_url, session_factory
from app.models import User
from tests.conftest import sqlite_url


def test_create_admin_cli(tmp_path: Path) -> None:
    database_url = sqlite_url(tmp_path / "cli.db")
    config = Config(str(Path(__file__).resolve().parents[1] / "alembic.ini"))
    config.set_main_option("sqlalchemy.url", database_url)
    command.upgrade(config, "head")

    assert main(
        [
            "create-admin",
            "--database-url",
            database_url,
            "--username",
            "Admin.User",
            "--display-name",
            "Admin User",
            "--password",
            "password1",
        ]
    ) == 0
    engine = create_engine_from_url(database_url)
    try:
        with session_factory(engine)() as db:
            user = db.scalar(select(User).where(User.username == "admin.user"))
            assert user is not None
            assert user.role == "SYSTEM_ADMIN"
    finally:
        engine.dispose()

