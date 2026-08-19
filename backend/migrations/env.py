"""Alembic environment configured through the application database settings."""

from __future__ import annotations

import sys
from pathlib import Path

from alembic import context

BACKEND_ROOT = Path(__file__).resolve().parents[1]
if str(BACKEND_ROOT) not in sys.path:
    sys.path.insert(0, str(BACKEND_ROOT))

from app.db.base import Base  # noqa: E402
from app import models  # noqa: E402,F401
from app.core.config import get_settings  # noqa: E402
from app.db.session import create_engine_from_url  # noqa: E402


config = context.config
target_metadata = Base.metadata


def _database_url() -> str:
    configured_url = config.get_main_option("sqlalchemy.url")
    default_url = "sqlite:///./data/mkaihub.db"
    if configured_url and configured_url != default_url:
        return configured_url
    return get_settings().database_url


def run_migrations_offline() -> None:
    """Run migrations without opening a database connection."""

    context.configure(
        url=_database_url(),
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
    )
    with context.begin_transaction():
        context.run_migrations()


def run_migrations_online() -> None:
    """Run migrations using the same SQLite pragmas as the application."""

    engine = create_engine_from_url(_database_url())
    with engine.connect() as connection:
        context.configure(connection=connection, target_metadata=target_metadata)
        with context.begin_transaction():
            context.run_migrations()
    engine.dispose()


if context.is_offline_mode():
    run_migrations_offline()
else:
    run_migrations_online()
