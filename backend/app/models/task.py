"""Task database model."""

from __future__ import annotations

from datetime import datetime
from decimal import Decimal

from sqlalchemy import (
    Boolean,
    CheckConstraint,
    DateTime,
    ForeignKey,
    Index,
    Integer,
    Numeric,
    String,
    Text,
    text,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.db.base import Base
from app.models.artifact import utcnow
from app.models.competition import Competition
from app.models.user import User


class Task(Base):
    """A lightweight task owned by its creator."""

    __tablename__ = "tasks"
    __table_args__ = (
        CheckConstraint(
            "status IN ('OPEN', 'IN_PROGRESS', 'REVIEWING', 'COMPLETED', 'CLOSED')",
            name="ck_tasks_status",
        ),
        CheckConstraint(
            "competition_id IS NULL OR (competition_max_score > 0 AND competition_weight > 0)",
            name="ck_tasks_competition_score",
        ),
        CheckConstraint(
            "competition_id IS NOT NULL "
            "OR (competition_required IS NULL AND competition_sort_order IS NULL "
            "AND competition_max_score IS NULL AND competition_weight IS NULL)",
            name="ck_tasks_competition_fields",
        ),
        Index("ix_tasks_status_updated", "status", "updated_at"),
        Index("ix_tasks_creator_updated", "creator_id", "updated_at"),
        Index("ix_tasks_competition", "competition_id", "competition_sort_order"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    title: Mapped[str] = mapped_column(String(200), nullable=False)
    description: Mapped[str] = mapped_column(Text, nullable=False)
    creator_id: Mapped[int] = mapped_column(ForeignKey("users.id"), nullable=False)
    status: Mapped[str] = mapped_column(String(32), nullable=False, default="OPEN", server_default="OPEN")
    # Competition linkage: NULL keeps the standalone-task semantics, while a set
    # value marks a competition task whose scoring columns must all be present.
    competition_id: Mapped[int | None] = mapped_column(
        ForeignKey("competitions.id", ondelete="RESTRICT"), nullable=True
    )
    competition_required: Mapped[bool | None] = mapped_column(Boolean, nullable=True)
    competition_sort_order: Mapped[int | None] = mapped_column(Integer, nullable=True)
    competition_max_score: Mapped[Decimal | None] = mapped_column(Numeric(6, 2), nullable=True)
    competition_weight: Mapped[Decimal | None] = mapped_column(Numeric(5, 2), nullable=True)
    deadline_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    completed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    closed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    creator: Mapped[User] = relationship()
    competition: Mapped[Competition | None] = relationship()
