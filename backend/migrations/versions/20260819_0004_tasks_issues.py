"""Create task and issue tables and extend comments with issue_id."""

from __future__ import annotations

from alembic import op
import sqlalchemy as sa


revision: str = "20260819_0004"
down_revision: str | None = "20260819_0003"
branch_labels: tuple[str, ...] | None = None
depends_on: str | None = None


def upgrade() -> None:
    op.create_table(
        "tasks",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("title", sa.String(length=200), nullable=False),
        sa.Column("description", sa.Text(), nullable=False),
        sa.Column("creator_id", sa.Integer(), nullable=False),
        sa.Column("status", sa.String(length=32), server_default="OPEN", nullable=False),
        sa.Column("deadline_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("completed_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("closed_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint("status IN ('OPEN', 'COMPLETED', 'CLOSED')", name="ck_tasks_status"),
        sa.ForeignKeyConstraint(["creator_id"], ["users.id"]),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index("ix_tasks_status_updated", "tasks", ["status", "updated_at"], unique=False)
    op.create_index("ix_tasks_creator_updated", "tasks", ["creator_id", "updated_at"], unique=False)

    op.create_table(
        "issues",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("title", sa.String(length=200), nullable=False),
        sa.Column("description", sa.Text(), nullable=False),
        sa.Column("author_id", sa.Integer(), nullable=False),
        sa.Column("status", sa.String(length=16), server_default="OPEN", nullable=False),
        sa.Column("closed_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint("status IN ('OPEN', 'CLOSED')", name="ck_issues_status"),
        sa.ForeignKeyConstraint(["author_id"], ["users.id"]),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index("ix_issues_status_updated", "issues", ["status", "updated_at"], unique=False)
    op.create_index("ix_issues_author_updated", "issues", ["author_id", "updated_at"], unique=False)

    # SQLite cannot alter column nullability or add table-level CHECK
    # constraints in place, so comments is rebuilt with the final shape:
    # artifact_id becomes nullable, issue_id is added, and exactly one of
    # the two target columns must be non-null.
    op.drop_index("ix_comments_artifact_created", table_name="comments")
    op.create_table(
        "comments_new",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("artifact_id", sa.Integer(), nullable=True),
        sa.Column("issue_id", sa.Integer(), nullable=True),
        sa.Column("author_id", sa.Integer(), nullable=False),
        sa.Column("content", sa.Text(), nullable=False),
        sa.Column("status", sa.String(length=16), server_default="VISIBLE", nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint("status IN ('VISIBLE', 'HIDDEN')", name="ck_comments_status"),
        sa.CheckConstraint("(artifact_id IS NULL) <> (issue_id IS NULL)", name="ck_comments_target"),
        sa.ForeignKeyConstraint(["artifact_id"], ["artifacts.id"], ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["issue_id"], ["issues.id"], ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["author_id"], ["users.id"]),
        sa.PrimaryKeyConstraint("id"),
    )
    op.execute(
        "INSERT INTO comments_new (id, artifact_id, issue_id, author_id, content, status, created_at, updated_at) "
        "SELECT id, artifact_id, NULL, author_id, content, status, created_at, updated_at FROM comments"
    )
    op.drop_table("comments")
    op.rename_table("comments_new", "comments")
    op.create_index("ix_comments_artifact_created", "comments", ["artifact_id", "created_at"], unique=False)
    op.create_index("ix_comments_issue_created", "comments", ["issue_id", "created_at"], unique=False)


def downgrade() -> None:
    op.drop_index("ix_comments_issue_created", table_name="comments")
    op.drop_index("ix_comments_artifact_created", table_name="comments")
    op.create_table(
        "comments_old",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("artifact_id", sa.Integer(), nullable=False),
        sa.Column("author_id", sa.Integer(), nullable=False),
        sa.Column("content", sa.Text(), nullable=False),
        sa.Column("status", sa.String(length=16), server_default="VISIBLE", nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint("status IN ('VISIBLE', 'HIDDEN')", name="ck_comments_status"),
        sa.ForeignKeyConstraint(["artifact_id"], ["artifacts.id"], ondelete="CASCADE"),
        sa.ForeignKeyConstraint(["author_id"], ["users.id"]),
        sa.PrimaryKeyConstraint("id"),
    )
    op.execute(
        "INSERT INTO comments_old (id, artifact_id, author_id, content, status, created_at, updated_at) "
        "SELECT id, artifact_id, author_id, content, status, created_at, updated_at FROM comments "
        "WHERE issue_id IS NULL"
    )
    op.drop_table("comments")
    op.rename_table("comments_old", "comments")
    op.create_index("ix_comments_artifact_created", "comments", ["artifact_id", "created_at"], unique=False)

    op.drop_index("ix_issues_author_updated", table_name="issues")
    op.drop_index("ix_issues_status_updated", table_name="issues")
    op.drop_table("issues")
    op.drop_index("ix_tasks_creator_updated", table_name="tasks")
    op.drop_index("ix_tasks_status_updated", table_name="tasks")
    op.drop_table("tasks")
