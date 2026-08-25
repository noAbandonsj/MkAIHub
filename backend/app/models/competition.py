"""Competition database model."""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import CheckConstraint, DateTime, ForeignKey, Index, Integer, String, Text, text
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.db.base import Base
from app.models.artifact import utcnow
from app.models.user import User


class Competition(Base):
    """A time-boxed internal competition announcement."""

    __tablename__ = "competitions"
    __table_args__ = (
        CheckConstraint("start_at < end_at", name="ck_competitions_window"),
        CheckConstraint(
            "status IN ('DRAFT', 'PUBLISHED', 'RESULT_PUBLISHED', 'ARCHIVED')",
            name="ck_competitions_status",
        ),
        Index("ix_competitions_start_end", "start_at", "end_at"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    title: Mapped[str] = mapped_column(String(200), nullable=False)
    summary: Mapped[str] = mapped_column(String(500), nullable=False)
    rules_markdown: Mapped[str] = mapped_column(Text, nullable=False)
    start_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    end_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    # Operational lifecycle answering "is the content public, are results out";
    # the derived UPCOMING/ONGOING/ENDED display state stays computed from time.
    status: Mapped[str] = mapped_column(String(32), nullable=False, default="DRAFT", server_default="PUBLISHED")
    created_by: Mapped[int] = mapped_column(ForeignKey("users.id"), nullable=False)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    creator: Mapped[User] = relationship()
