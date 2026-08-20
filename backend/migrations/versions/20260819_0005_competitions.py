"""Create the competitions table."""

from __future__ import annotations

from alembic import op
import sqlalchemy as sa


revision: str = "20260819_0005"
down_revision: str | None = "20260819_0004"
branch_labels: tuple[str, ...] | None = None
depends_on: str | None = None


def upgrade() -> None:
    op.create_table(
        "competitions",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("title", sa.String(length=200), nullable=False),
        sa.Column("summary", sa.String(length=500), nullable=False),
        sa.Column("rules_markdown", sa.Text(), nullable=False),
        sa.Column("start_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("end_at", sa.DateTime(timezone=True), nullable=False),
        sa.Column("created_by", sa.Integer(), nullable=False),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.text("CURRENT_TIMESTAMP"), nullable=False),
        # Display status is computed from start_at/end_at and never stored;
        # the constraint only protects the invariant that a competition starts
        # before it ends.
        sa.CheckConstraint("start_at < end_at", name="ck_competitions_window"),
        sa.ForeignKeyConstraint(["created_by"], ["users.id"]),
        sa.PrimaryKeyConstraint("id"),
    )
    op.create_index("ix_competitions_start_end", "competitions", ["start_at", "end_at"], unique=False)


def downgrade() -> None:
    op.drop_index("ix_competitions_start_end", table_name="competitions")
    op.drop_table("competitions")
