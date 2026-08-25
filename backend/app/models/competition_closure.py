"""Competition registration, review, and result models for the closure loop."""

from __future__ import annotations

from datetime import datetime
from decimal import Decimal

from sqlalchemy import (
    CheckConstraint,
    DateTime,
    ForeignKey,
    Index,
    Integer,
    Numeric,
    String,
    Text,
    UniqueConstraint,
    text,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.db.base import Base
from app.models.artifact import utcnow
from app.models.competition import Competition
from app.models.task_closure import TaskSubmission
from app.models.user import User


class CompetitionRegistration(Base):
    """One user's current registration record on one competition."""

    __tablename__ = "competition_registrations"
    __table_args__ = (
        CheckConstraint("status IN ('REGISTERED', 'CANCELLED')", name="ck_competition_registrations_status"),
        UniqueConstraint("competition_id", "user_id", name="uq_competition_registrations_competition_user"),
        Index("ix_competition_registrations_user", "user_id"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    competition_id: Mapped[int] = mapped_column(
        ForeignKey("competitions.id", ondelete="RESTRICT"), nullable=False
    )
    user_id: Mapped[int] = mapped_column(ForeignKey("users.id", ondelete="RESTRICT"), nullable=False)
    status: Mapped[str] = mapped_column(
        String(32), nullable=False, default="REGISTERED", server_default="REGISTERED"
    )
    registered_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False, default=utcnow)
    cancelled_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    competition: Mapped[Competition] = relationship()
    user: Mapped[User] = relationship()


class CompetitionReview(Base):
    """The single official review row of one task submission."""

    __tablename__ = "competition_reviews"
    __table_args__ = (
        UniqueConstraint("task_submission_id", name="uq_competition_reviews_submission"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    task_submission_id: Mapped[int] = mapped_column(
        ForeignKey("task_submissions.id", ondelete="RESTRICT"), nullable=False
    )
    reviewer_id: Mapped[int] = mapped_column(ForeignKey("users.id", ondelete="RESTRICT"), nullable=False)
    raw_score: Mapped[Decimal] = mapped_column(Numeric(6, 2), nullable=False)
    comment: Mapped[str | None] = mapped_column(Text, nullable=True)
    reviewed_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False, default=utcnow)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    submission: Mapped[TaskSubmission] = relationship(back_populates="competition_review")
    reviewer: Mapped[User] = relationship()


class CompetitionResult(Base):
    """A frozen leaderboard snapshot row published once per competition."""

    __tablename__ = "competition_results"
    __table_args__ = (
        CheckConstraint("rank >= 1", name="ck_competition_results_rank"),
        UniqueConstraint("competition_id", "registration_id", name="uq_competition_results_competition_registration"),
        Index("ix_competition_results_rank", "competition_id", "rank"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    competition_id: Mapped[int] = mapped_column(
        ForeignKey("competitions.id", ondelete="RESTRICT"), nullable=False
    )
    registration_id: Mapped[int] = mapped_column(
        ForeignKey("competition_registrations.id", ondelete="RESTRICT"), nullable=False
    )
    total_score: Mapped[Decimal] = mapped_column(Numeric(7, 4), nullable=False)
    rank: Mapped[int] = mapped_column(Integer, nullable=False)
    award: Mapped[str | None] = mapped_column(String(200), nullable=True)
    published_by: Mapped[int] = mapped_column(ForeignKey("users.id", ondelete="RESTRICT"), nullable=False)
    published_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False, default=utcnow)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    competition: Mapped[Competition] = relationship()
    registration: Mapped[CompetitionRegistration] = relationship()
    publisher: Mapped[User] = relationship()
