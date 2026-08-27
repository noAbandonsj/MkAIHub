"""Keep competition registrations and task participation in sync.

Revision ID: 20260819_0008
Revises: 20260819_0007
"""

from alembic import op


revision: str = "20260819_0008"
down_revision: str | None = "20260819_0007"
branch_labels: str | None = None
depends_on: str | None = None


def upgrade() -> None:
    # Repair active registrations that predate automatic task participation.
    op.execute(
        """
        INSERT INTO task_participants
            (task_id, user_id, status, joined_at, left_at, created_at, updated_at)
        SELECT
            tasks.id,
            registrations.user_id,
            'ACTIVE',
            registrations.registered_at,
            NULL,
            registrations.registered_at,
            registrations.registered_at
        FROM competition_registrations AS registrations
        JOIN tasks ON tasks.competition_id = registrations.competition_id
        LEFT JOIN task_participants AS participants
            ON participants.task_id = tasks.id
            AND participants.user_id = registrations.user_id
        WHERE registrations.status = 'REGISTERED'
            AND participants.id IS NULL
        """
    )
    op.execute(
        """
        UPDATE task_participants
        SET
            status = 'ACTIVE',
            joined_at = (
                SELECT registrations.registered_at
                FROM tasks
                JOIN competition_registrations AS registrations
                    ON registrations.competition_id = tasks.competition_id
                WHERE tasks.id = task_participants.task_id
                    AND registrations.user_id = task_participants.user_id
                    AND registrations.status = 'REGISTERED'
            ),
            left_at = NULL,
            updated_at = CURRENT_TIMESTAMP
        WHERE status = 'LEFT'
            AND EXISTS (
                SELECT 1
                FROM tasks
                JOIN competition_registrations AS registrations
                    ON registrations.competition_id = tasks.competition_id
                WHERE tasks.id = task_participants.task_id
                    AND registrations.user_id = task_participants.user_id
                    AND registrations.status = 'REGISTERED'
            )
        """
    )
    # Old manual joins without a registration cannot have submissions through
    # the public API; deactivate those orphan participation rows.
    op.execute(
        """
        UPDATE task_participants
        SET status = 'LEFT', left_at = CURRENT_TIMESTAMP, updated_at = CURRENT_TIMESTAMP
        WHERE status = 'ACTIVE'
            AND EXISTS (
                SELECT 1 FROM tasks
                WHERE tasks.id = task_participants.task_id
                    AND tasks.competition_id IS NOT NULL
            )
            AND NOT EXISTS (
                SELECT 1
                FROM tasks
                JOIN competition_registrations AS registrations
                    ON registrations.competition_id = tasks.competition_id
                WHERE tasks.id = task_participants.task_id
                    AND registrations.user_id = task_participants.user_id
                    AND registrations.status = 'REGISTERED'
            )
            AND NOT EXISTS (
                SELECT 1 FROM task_submissions
                WHERE task_submissions.participant_id = task_participants.id
            )
        """
    )


def downgrade() -> None:
    # This revision only repairs business data; removing valid participation
    # rows on downgrade would destroy user history.
    pass
