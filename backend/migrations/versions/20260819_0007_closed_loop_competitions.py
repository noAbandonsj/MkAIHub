"""Competition closure: task competition columns, competition status, three tables."""

from __future__ import annotations

from alembic import op
import sqlalchemy as sa


revision: str = "20260819_0007"
down_revision: str | None = "20260819_0006"
branch_labels: tuple[str, ...] | None = None
depends_on: str | None = None


def _rebuild_tasks(*, with_competition: bool) -> None:
    """SQLite cannot add table-level CHECK constraints in place, so tasks is
    rebuilt with (or without) the five competition columns while foreign keys
    are disabled; task_participants/task_submissions still reference it."""

    op.drop_index("ix_tasks_status_updated", table_name="tasks")
    op.drop_index("ix_tasks_creator_updated", table_name="tasks")
    columns = [
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("title", sa.String(length=200), nullable=False),
        sa.Column("description", sa.Text(), nullable=False),
        sa.Column("creator_id", sa.Integer(), nullable=False),
        sa.Column("status", sa.String(length=32), server_default="OPEN", nullable=False),
    ]
    if with_competition:
        columns.extend(
            [
                sa.Column("competition_id", sa.Integer(), nullable=True),
                sa.Column("competition_required", sa.Boolean(), nullable=True),
                sa.Column("competition_sort_order", sa.Integer(), nullable=True),
                sa.Column("competition_max_score", sa.Numeric(6, 2), nullable=True),
                sa.Column("competition_weight", sa.Numeric(5, 2), nullable=True),
            ]
        )
    columns.extend(
        [
            sa.Column("deadline_at", sa.DateTime(timezone=True), nullable=True),
            sa.Column("completed_at", sa.DateTime(timezone=True), nullable=True),
            sa.Column("closed_at", sa.DateTime(timezone=True), nullable=True),
            sa.Column(
                "created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False
            ),
            sa.Column(
                "updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False
            ),
        ]
    )
    constraints = [
        sa.CheckConstraint(
            "status IN ('OPEN', 'IN_PROGRESS', 'REVIEWING', 'COMPLETED', 'CLOSED')",
            name="ck_tasks_status",
        ),
        sa.ForeignKeyConstraint(["creator_id"], ["users.id"]),
        sa.PrimaryKeyConstraint("id"),
    ]
    if with_competition:
        constraints.extend(
            [
                sa.CheckConstraint(
                    "competition_id IS NULL OR (competition_max_score > 0 AND competition_weight > 0)",
                    name="ck_tasks_competition_score",
                ),
                sa.CheckConstraint(
                    "competition_id IS NOT NULL "
                    "OR (competition_required IS NULL AND competition_sort_order IS NULL "
                    "AND competition_max_score IS NULL AND competition_weight IS NULL)",
                    name="ck_tasks_competition_fields",
                ),
                sa.ForeignKeyConstraint(["competition_id"], ["competitions.id"], ondelete="RESTRICT"),
            ]
        )
    op.create_table("tasks_new", *columns, *constraints)
    base_names = (
        "id, title, description, creator_id, status, deadline_at, completed_at, "
        "closed_at, created_at, updated_at"
    )
    if with_competition:
        op.execute(
            f"INSERT INTO tasks_new ({base_names}, competition_id, competition_required, "
            "competition_sort_order, competition_max_score, competition_weight) "
            f"SELECT {base_names}, NULL, NULL, NULL, NULL, NULL FROM tasks"
        )
    else:
        op.execute(f"INSERT INTO tasks_new ({base_names}) SELECT {base_names} FROM tasks")
    op.drop_table("tasks")
    op.rename_table("tasks_new", "tasks")
    op.create_index("ix_tasks_status_updated", "tasks", ["status", "updated_at"], unique=False)
    op.create_index("ix_tasks_creator_updated", "tasks", ["creator_id", "updated_at"], unique=False)
    if with_competition:
        op.create_index(
            "ix_tasks_competition", "tasks", ["competition_id", "competition_sort_order"], unique=False
        )


def upgrade() -> None:
    # The migration connection enforces foreign keys, and tasks is referenced
    # by task_participants/task_submissions; the rebuild needs them off.
    with op.get_context().autocommit_block():
        op.execute("PRAGMA foreign_keys = OFF")
    try:
        _rebuild_tasks(with_competition=True)

        # SQLite cannot ALTER ADD a named table constraint; a column-level
        # CHECK inside ADD COLUMN enforces the same value set (unnamed at the
        # storage level, matching the ORM constraint semantically).
        op.execute(
            "ALTER TABLE competitions ADD COLUMN status VARCHAR(32) NOT NULL DEFAULT 'PUBLISHED' "
            "CHECK (status IN ('DRAFT', 'PUBLISHED', 'RESULT_PUBLISHED', 'ARCHIVED'))"
        )

        op.create_table(
            "competition_registrations",
            sa.Column("id", sa.Integer(), nullable=False),
            sa.Column("competition_id", sa.Integer(), nullable=False),
            sa.Column("user_id", sa.Integer(), nullable=False),
            sa.Column("status", sa.String(length=32), server_default="REGISTERED", nullable=False),
            sa.Column("registered_at", sa.DateTime(timezone=True), nullable=False),
            sa.Column("cancelled_at", sa.DateTime(timezone=True), nullable=True),
            sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
            sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
            sa.CheckConstraint("status IN ('REGISTERED', 'CANCELLED')", name="ck_competition_registrations_status"),
            sa.ForeignKeyConstraint(["competition_id"], ["competitions.id"], ondelete="RESTRICT"),
            sa.ForeignKeyConstraint(["user_id"], ["users.id"], ondelete="RESTRICT"),
            sa.PrimaryKeyConstraint("id"),
            sa.UniqueConstraint("competition_id", "user_id", name="uq_competition_registrations_competition_user"),
        )
        op.create_index(
            "ix_competition_registrations_user", "competition_registrations", ["user_id"], unique=False
        )

        op.create_table(
            "competition_reviews",
            sa.Column("id", sa.Integer(), nullable=False),
            sa.Column("task_submission_id", sa.Integer(), nullable=False),
            sa.Column("reviewer_id", sa.Integer(), nullable=False),
            sa.Column("raw_score", sa.Numeric(6, 2), nullable=False),
            sa.Column("comment", sa.Text(), nullable=True),
            sa.Column("reviewed_at", sa.DateTime(timezone=True), nullable=False),
            sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
            sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
            sa.ForeignKeyConstraint(["task_submission_id"], ["task_submissions.id"], ondelete="RESTRICT"),
            sa.ForeignKeyConstraint(["reviewer_id"], ["users.id"], ondelete="RESTRICT"),
            sa.PrimaryKeyConstraint("id"),
            sa.UniqueConstraint("task_submission_id", name="uq_competition_reviews_submission"),
        )

        op.create_table(
            "competition_results",
            sa.Column("id", sa.Integer(), nullable=False),
            sa.Column("competition_id", sa.Integer(), nullable=False),
            sa.Column("registration_id", sa.Integer(), nullable=False),
            sa.Column("total_score", sa.Numeric(7, 4), nullable=False),
            sa.Column("rank", sa.Integer(), nullable=False),
            sa.Column("award", sa.String(length=200), nullable=True),
            sa.Column("published_by", sa.Integer(), nullable=False),
            sa.Column("published_at", sa.DateTime(timezone=True), nullable=False),
            sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
            sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
            sa.CheckConstraint("rank >= 1", name="ck_competition_results_rank"),
            sa.ForeignKeyConstraint(["competition_id"], ["competitions.id"], ondelete="RESTRICT"),
            sa.ForeignKeyConstraint(["registration_id"], ["competition_registrations.id"], ondelete="RESTRICT"),
            sa.ForeignKeyConstraint(["published_by"], ["users.id"], ondelete="RESTRICT"),
            sa.PrimaryKeyConstraint("id"),
            sa.UniqueConstraint(
                "competition_id", "registration_id", name="uq_competition_results_competition_registration"
            ),
        )
        op.create_index(
            "ix_competition_results_rank", "competition_results", ["competition_id", "rank"], unique=False
        )
    finally:
        with op.get_context().autocommit_block():
            op.execute("PRAGMA foreign_keys = ON")


def downgrade() -> None:
    with op.get_context().autocommit_block():
        op.execute("PRAGMA foreign_keys = OFF")
    try:
        op.drop_index("ix_competition_results_rank", table_name="competition_results")
        op.drop_table("competition_results")
        op.drop_table("competition_reviews")
        op.drop_index("ix_competition_registrations_user", table_name="competition_registrations")
        op.drop_table("competition_registrations")

        _rebuild_tasks(with_competition=False)

        # competitions must be rebuilt to drop the status CHECK cleanly; by now
        # nothing references it anymore because the tasks rebuild dropped the
        # competition_id foreign key.
        op.drop_index("ix_competitions_start_end", table_name="competitions")
        op.create_table(
            "competitions_old",
            sa.Column("id", sa.Integer(), nullable=False),
            sa.Column("title", sa.String(length=200), nullable=False),
            sa.Column("summary", sa.String(length=500), nullable=False),
            sa.Column("rules_markdown", sa.Text(), nullable=False),
            sa.Column("start_at", sa.DateTime(timezone=True), nullable=False),
            sa.Column("end_at", sa.DateTime(timezone=True), nullable=False),
            sa.Column("created_by", sa.Integer(), nullable=False),
            sa.Column(
                "created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False
            ),
            sa.Column(
                "updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False
            ),
            sa.CheckConstraint("start_at < end_at", name="ck_competitions_window"),
            sa.ForeignKeyConstraint(["created_by"], ["users.id"]),
            sa.PrimaryKeyConstraint("id"),
        )
        op.execute(
            "INSERT INTO competitions_old (id, title, summary, rules_markdown, start_at, end_at, created_by, created_at, updated_at) "
            "SELECT id, title, summary, rules_markdown, start_at, end_at, created_by, created_at, updated_at FROM competitions"
        )
        op.drop_table("competitions")
        op.rename_table("competitions_old", "competitions")
        op.create_index(
            "ix_competitions_start_end", "competitions", ["start_at", "end_at"], unique=False
        )
    finally:
        with op.get_context().autocommit_block():
            op.execute("PRAGMA foreign_keys = ON")
