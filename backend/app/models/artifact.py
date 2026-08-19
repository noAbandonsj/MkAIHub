"""Artifact, attachment, and artifact-comment database models."""

from __future__ import annotations

from datetime import UTC, datetime

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
from app.models.user import User


def utcnow() -> datetime:
    return datetime.now(UTC)


class StoredFile(Base):
    """A file stored below the configured upload directory."""

    __tablename__ = "files"
    __table_args__ = (
        Index("uq_files_relative_path", "relative_path", unique=True),
        Index("ix_files_uploader_created", "uploader_id", "created_at"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    original_name: Mapped[str] = mapped_column(String(255), nullable=False)
    stored_name: Mapped[str] = mapped_column(String(100), nullable=False)
    relative_path: Mapped[str] = mapped_column(String(500), nullable=False)
    extension: Mapped[str] = mapped_column(String(32), nullable=False)
    mime_type: Mapped[str] = mapped_column(String(150), nullable=False)
    size_bytes: Mapped[int] = mapped_column(Integer, nullable=False)
    sha256: Mapped[str] = mapped_column(String(64), nullable=False)
    uploader_id: Mapped[int] = mapped_column(ForeignKey("users.id"), nullable=False)
    is_deleted: Mapped[bool] = mapped_column(Boolean, nullable=False, default=False, server_default=text("0"))
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    uploader: Mapped[User] = relationship()
    artifact_links: Mapped[list[ArtifactFile]] = relationship(back_populates="file")


class Artifact(Base):
    """A lightweight internal AI sharing item."""

    __tablename__ = "artifacts"
    __table_args__ = (
        CheckConstraint(
            "status IN ('DRAFT', 'PUBLISHED', 'ARCHIVED')",
            name="ck_artifacts_status",
        ),
        Index("ix_artifacts_status_published", "status", "published_at"),
        Index("ix_artifacts_author_updated", "author_id", "updated_at"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    title: Mapped[str] = mapped_column(String(200), nullable=False)
    summary: Mapped[str] = mapped_column(String(500), nullable=False)
    content_markdown: Mapped[str] = mapped_column(Text, nullable=False)
    author_id: Mapped[int] = mapped_column(ForeignKey("users.id"), nullable=False)
    status: Mapped[str] = mapped_column(String(32), nullable=False, default="DRAFT", server_default="DRAFT")
    published_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    archived_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    author: Mapped[User] = relationship()
    file_links: Mapped[list[ArtifactFile]] = relationship(
        back_populates="artifact",
        cascade="all, delete-orphan",
        order_by="ArtifactFile.sort_order",
        passive_deletes=True,
    )
    comments: Mapped[list[Comment]] = relationship(
        back_populates="artifact",
        cascade="all, delete-orphan",
        passive_deletes=True,
    )


class ArtifactFile(Base):
    """Ordered attachment link between an artifact and a stored file."""

    __tablename__ = "artifact_files"
    __table_args__ = (
        UniqueConstraint("artifact_id", "file_id", name="uq_artifact_files_artifact_file"),
        Index("ix_artifact_files_file_id", "file_id"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    artifact_id: Mapped[int] = mapped_column(
        ForeignKey("artifacts.id", ondelete="CASCADE"), nullable=False
    )
    file_id: Mapped[int] = mapped_column(ForeignKey("files.id"), nullable=False)
    sort_order: Mapped[int] = mapped_column(Integer, nullable=False, default=0, server_default="0")

    artifact: Mapped[Artifact] = relationship(back_populates="file_links")
    file: Mapped[StoredFile] = relationship(back_populates="artifact_links")


class Comment(Base):
    """A first-version comment attached to an artifact."""

    __tablename__ = "comments"
    __table_args__ = (
        CheckConstraint("status IN ('VISIBLE', 'HIDDEN')", name="ck_comments_status"),
        Index("ix_comments_artifact_created", "artifact_id", "created_at"),
    )

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    artifact_id: Mapped[int] = mapped_column(
        ForeignKey("artifacts.id", ondelete="CASCADE"), nullable=False
    )
    author_id: Mapped[int] = mapped_column(ForeignKey("users.id"), nullable=False)
    content: Mapped[str] = mapped_column(Text, nullable=False)
    status: Mapped[str] = mapped_column(String(16), nullable=False, default="VISIBLE", server_default="VISIBLE")
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True), nullable=False, default=utcnow, onupdate=utcnow, server_default=text("CURRENT_TIMESTAMP")
    )

    artifact: Mapped[Artifact] = relationship(back_populates="comments")
    author: Mapped[User] = relationship()
