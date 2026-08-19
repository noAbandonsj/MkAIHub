"""Shared isolated application fixtures for the batch-0 test suite."""

from __future__ import annotations

from pathlib import Path

import pytest
from fastapi.testclient import TestClient
from alembic import command
from alembic.config import Config

from app.core.config import Settings
from app.main import create_app


def sqlite_url(path: Path) -> str:
    """Build a portable absolute SQLite URL for a temporary test database."""

    return f"sqlite:///{path.resolve().as_posix()}"


@pytest.fixture
def test_settings(tmp_path: Path) -> Settings:
    return Settings(
        _env_file=None,
        app_env="test",
        app_name="MkAIHub Test",
        database_url=sqlite_url(tmp_path / "mkaihub-test.db"),
        data_dir=tmp_path / "data",
        upload_dir=tmp_path / "uploads",
        app_secret_key="test-secret-key-for-csrf-32-bytes-minimum",
        log_level="WARNING",
    )


@pytest.fixture
def client(test_settings: Settings):
    config = Config(str(Path(__file__).resolve().parents[1] / "alembic.ini"))
    config.set_main_option("sqlalchemy.url", test_settings.database_url)
    command.upgrade(config, "head")
    application = create_app(test_settings)
    with TestClient(application) as test_client:
        yield test_client
    application.state.db_engine.dispose()
