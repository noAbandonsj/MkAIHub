"""Issue database model."""

from __future__ import annotations

from datetime import datetime
from typing import TYPE_CHECKING

from sqlalchemy import (
    CheckConstraint,
    DateTime,
    ForeignKey,
    Index,
    Integer,
    String,
    Text,
    text,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from app.db.base import Base
from app.models.artifact import utcnow
from app.models.user import User

if TYPE_CHECKING:
    from app.models.artifact import Comment


class Issue(Base):
    """An internal problem report or discussion thread."""

    __tablename__ = "issues"
    __table_args__ = (
        CheckConstraint("status IN ('OPEN', 'CLOSED')", name="ck_issues_status"),
        Index("ix_issues_status_updated", "status", "updated_at"),
        Index("ix_issues_author_updated", "author_id", "updated_at"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    title: Mapped[str] = mapped_column(String(200), nullable=False)
    description: Mapped[str] = mapped_column(Text, nullable=False)
    author_id: Mapped[int] = mapped_column(ForeignKey("users.id"), nullable=False)
    status: Mapped[str] = mapped_column(String(16), nullable=False, default="OPEN", server_default="OPEN")
    closed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    author: Mapped[User] = relationship()
    comments: Mapped[list[Comment]] = relationship(
        back_populates="issue",
        cascade="all, delete-orphan",
        passive_deletes=True,
    )
