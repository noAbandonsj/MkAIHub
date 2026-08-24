"""Extend task status values and add participation and submission tables."""

from __future__ import annotations

from alembic import op
import sqlalchemy as sa


revision: str = "20260819_0006"
down_revision: str | None = "20260819_0005"
branch_labels: tuple[str, ...] | None = None
depends_on: str | None = None


def upgrade() -> None:
    # SQLite cannot replace a CHECK constraint in place, so tasks is rebuilt
    # with the extended closure status list; existing row values stay valid.
    op.drop_index("ix_tasks_status_updated", table_name="tasks")
    op.drop_index("ix_tasks_creator_updated", table_name="tasks")
    op.create_table(
        "tasks_new",
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
        sa.CheckConstraint(
            "status IN ('OPEN', 'IN_PROGRESS', 'REVIEWING', 'COMPLETED', 'CLOSED')",
            name="ck_tasks_status",
        ),
        sa.ForeignKeyConstraint(["creator_id"], ["users.id"]),
        sa.PrimaryKeyConstraint("id"),
    )
    op.execute(
        "INSERT INTO tasks_new (id, title, description, creator_id, status, deadline_at, completed_at, closed_at, created_at, updated_at) "
        "SELECT id, title, description, creator_id, status, deadline_at, completed_at, closed_at, created_at, updated_at FROM tasks"
    )
    op.drop_table("tasks")
    op.rename_table("tasks_new", "tasks")
    op.create_index("ix_tasks_status_updated", "tasks", ["status", "updated_at"], unique=False)
    op.create_index("ix_tasks_creator_updated", "tasks", ["creator_id", "updated_at"], unique=False)

    op.create_table(
        "task_participants",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("task_id", sa.Integer(), nullable=False),
        sa.Column("user_id", sa.Integer(), nullable=False),
        sa.Column("status", sa.String(length=32), server_default="ACTIVE", nullable=False),
        sa.Column("joined_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("left_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint("status IN ('ACTIVE', 'LEFT')", name="ck_task_participants_status"),
        sa.ForeignKeyConstraint(["task_id"], ["tasks.id"], ondelete="RESTRICT"),
        sa.ForeignKeyConstraint(["user_id"], ["users.id"], ondelete="RESTRICT"),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("task_id", "user_id", name="uq_task_participants_task_user"),
    )
    op.create_index("ix_task_participants_user", "task_participants", ["user_id", "status"], unique=False)

    op.create_table(
        "task_submissions",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("task_id", sa.Integer(), nullable=False),
        sa.Column("participant_id", sa.Integer(), nullable=False),
        sa.Column("artifact_id", sa.Integer(), nullable=False),
        sa.Column("round_no", sa.Integer(), nullable=False),
        sa.Column("note", sa.Text(), nullable=True),
        sa.Column("status", sa.String(length=32), server_default="SUBMITTED", nullable=False),
        sa.Column("is_current", sa.Boolean(), server_default=sa.text("1"), nullable=False),
        sa.Column("submitted_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("revision_requested_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("decided_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("decider_id", sa.Integer(), nullable=True),
        sa.Column("decision_note", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.CheckConstraint(
            "status IN ('SUBMITTED', 'REVISION_REQUIRED', 'ACCEPTED', 'REJECTED')",
            name="ck_task_submissions_status",
        ),
        sa.CheckConstraint("round_no >= 1", name="ck_task_submissions_round"),
        sa.ForeignKeyConstraint(["task_id"], ["tasks.id"], ondelete="RESTRICT"),
        sa.ForeignKeyConstraint(["participant_id"], ["task_participants.id"], ondelete="RESTRICT"),
        sa.ForeignKeyConstraint(["artifact_id"], ["artifacts.id"], ondelete="RESTRICT"),
        sa.ForeignKeyConstraint(["decider_id"], ["users.id"], ondelete="RESTRICT"),
        sa.PrimaryKeyConstraint("id"),
        sa.UniqueConstraint("participant_id", "round_no", name="uq_task_submissions_round"),
    )
    op.create_index("ix_task_submissions_task_status", "task_submissions", ["task_id", "status"], unique=False)
    op.create_index("ix_task_submissions_participant", "task_submissions", ["participant_id"], unique=False)
    op.create_index("ix_task_submissions_artifact", "task_submissions", ["artifact_id"], unique=False)


def downgrade() -> None:
    op.drop_index("ix_task_submissions_artifact", table_name="task_submissions")
    op.drop_index("ix_task_submissions_participant", table_name="task_submissions")
    op.drop_index("ix_task_submissions_task_status", table_name="task_submissions")
    op.drop_table("task_submissions")
    op.drop_index("ix_task_participants_user", table_name="task_participants")
    op.drop_table("task_participants")

    op.drop_index("ix_tasks_status_updated", table_name="tasks")
    op.drop_index("ix_tasks_creator_updated", table_name="tasks")
    op.create_table(
        "tasks_old",
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
    op.execute(
        "INSERT INTO tasks_old (id, title, description, creator_id, status, deadline_at, completed_at, closed_at, created_at, updated_at) "
        "SELECT id, title, description, creator_id, status, deadline_at, completed_at, closed_at, created_at, updated_at FROM tasks "
        "WHERE status IN ('OPEN', 'COMPLETED', 'CLOSED')"
    )
    op.drop_table("tasks")
    op.rename_table("tasks_old", "tasks")
    op.create_index("ix_tasks_status_updated", "tasks", ["status", "updated_at"], unique=False)
    op.create_index("ix_tasks_creator_updated", "tasks", ["creator_id", "updated_at"], unique=False)
