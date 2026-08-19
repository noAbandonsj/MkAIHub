"""SQLAlchemy declarative base shared by all application models."""

from __future__ import annotations

from sqlalchemy.orm import DeclarativeBase


class Base(DeclarativeBase):
    """Base class used by SQLAlchemy models and Alembic metadata."""

