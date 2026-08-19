"""Create the empty batch-0 schema baseline.

Business tables are intentionally introduced by their respective later
implementation batches. This revision establishes a stable Alembic head from
which those migrations can evolve.
"""

from __future__ import annotations


# revision identifiers, used by Alembic.
revision: str = "20260819_0001"
down_revision: str | None = None
branch_labels: tuple[str, ...] | None = None
depends_on: str | None = None


def upgrade() -> None:
    """No business tables belong to the batch-0 engineering baseline."""


def downgrade() -> None:
    """The baseline has no application-owned objects to remove."""
