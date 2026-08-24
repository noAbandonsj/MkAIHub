"""Task participation and submission models for the closure loop."""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import (
    Boolean,
    CheckConstraint,
    DateTime,
    ForeignKey,
    Index,
    Integer,
    String,
    Text,
    UniqueConstraint,
    text,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.db.base import Base
from app.models.artifact import Artifact, utcnow
from app.models.task import Task
from app.models.user import User


class TaskParticipant(Base):
    """One user's current participation record on one task."""

    __tablename__ = "task_participants"
    __table_args__ = (
        CheckConstraint("status IN ('ACTIVE', 'LEFT')", name="ck_task_participants_status"),
        UniqueConstraint("task_id", "user_id", name="uq_task_participants_task_user"),
        Index("ix_task_participants_user", "user_id", "status"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    task_id: Mapped[int] = mapped_column(ForeignKey("tasks.id", ondelete="RESTRICT"), nullable=False)
    user_id: Mapped[int] = mapped_column(ForeignKey("users.id", ondelete="RESTRICT"), nullable=False)
    status: Mapped[str] = mapped_column(String(32), nullable=False, default="ACTIVE", server_default="ACTIVE")
    joined_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False, default=utcnow)
    left_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    task: Mapped[Task] = relationship()
    user: Mapped[User] = relationship()


class TaskSubmission(Base):
    """One submitted artifact round of one participant on one task."""

    __tablename__ = "task_submissions"
    __table_args__ = (
        CheckConstraint(
            "status IN ('SUBMITTED', 'REVISION_REQUIRED', 'ACCEPTED', 'REJECTED')",
            name="ck_task_submissions_status",
        ),
        CheckConstraint("round_no >= 1", name="ck_task_submissions_round"),
        UniqueConstraint("participant_id", "round_no", name="uq_task_submissions_round"),
        Index("ix_task_submissions_task_status", "task_id", "status"),
        Index("ix_task_submissions_participant", "participant_id"),
        Index("ix_task_submissions_artifact", "artifact_id"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    task_id: Mapped[int] = mapped_column(ForeignKey("tasks.id", ondelete="RESTRICT"), nullable=False)
    participant_id: Mapped[int] = mapped_column(
        ForeignKey("task_participants.id", ondelete="RESTRICT"), nullable=False
    )
    artifact_id: Mapped[int] = mapped_column(ForeignKey("artifacts.id", ondelete="RESTRICT"), nullable=False)
    round_no: Mapped[int] = mapped_column(Integer, nullable=False)
    note: Mapped[str | None] = mapped_column(Text, nullable=True)
    status: Mapped[str] = mapped_column(String(32), nullable=False, default="SUBMITTED", server_default="SUBMITTED")
    is_current: Mapped[bool] = mapped_column(Boolean, nullable=False, default=True, server_default=text("1"))
    submitted_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False, default=utcnow)
    revision_requested_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    decided_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    decider_id: Mapped[int | None] = mapped_column(ForeignKey("users.id", ondelete="RESTRICT"), nullable=True)
    decision_note: Mapped[str | None] = mapped_column(Text, nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    task: Mapped[Task] = relationship()
    participant: Mapped[TaskParticipant] = relationship()
    artifact: Mapped[Artifact] = relationship()
    decider: Mapped[User | None] = relationship()
