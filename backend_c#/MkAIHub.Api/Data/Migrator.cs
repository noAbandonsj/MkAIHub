using Microsoft.Data.Sqlite;

namespace MkAIHub.Api.Data;

/// <summary>
/// Applies the same revision chain as the Python Alembic migrations and
/// tracks progress in the alembic_version table, so both backends can share
/// one SQLite database file.
/// </summary>
public static class Migrator
{
    public const string Head = "20260819_0008";

    private sealed record Revision(string Id, string? DownRevision, string[] Statements);

    private static readonly Revision[] Revisions =
    {
        new(
            "20260819_0001",
            null,
            Array.Empty<string>()),
        new(
            "20260819_0002",
            "20260819_0001",
            new[]
            {
                @"CREATE TABLE users (
                    id INTEGER NOT NULL,
                    username VARCHAR(64) NOT NULL,
                    display_name VARCHAR(100) NOT NULL,
                    password_hash VARCHAR(255) NOT NULL,
                    role VARCHAR(32) NOT NULL,
                    is_active BOOLEAN DEFAULT '1' NOT NULL,
                    last_login_at DATETIME,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_users_role CHECK (role IN ('EMPLOYEE', 'SYSTEM_ADMIN')),
                    CONSTRAINT pk_users PRIMARY KEY (id)
                )",
                "CREATE UNIQUE INDEX uq_users_username ON users (username)",
                "CREATE INDEX ix_users_role_active ON users (role, is_active)",
                @"CREATE TABLE user_sessions (
                    id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    token_hash VARCHAR(128) NOT NULL,
                    expires_at DATETIME NOT NULL,
                    last_seen_at DATETIME NOT NULL,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    revoked_at DATETIME,
                    CONSTRAINT fk_user_sessions_user_id FOREIGN KEY(user_id) REFERENCES users (id) ON DELETE CASCADE,
                    CONSTRAINT pk_user_sessions PRIMARY KEY (id)
                )",
                "CREATE UNIQUE INDEX uq_user_sessions_token_hash ON user_sessions (token_hash)",
                "CREATE INDEX ix_user_sessions_user_id ON user_sessions (user_id)",
                "CREATE INDEX ix_user_sessions_user_active ON user_sessions (user_id, revoked_at, expires_at)",
            }),
        new(
            "20260819_0003",
            "20260819_0002",
            new[]
            {
                @"CREATE TABLE files (
                    id INTEGER NOT NULL,
                    original_name VARCHAR(255) NOT NULL,
                    stored_name VARCHAR(100) NOT NULL,
                    relative_path VARCHAR(500) NOT NULL,
                    extension VARCHAR(32) NOT NULL,
                    mime_type VARCHAR(150) NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    sha256 VARCHAR(64) NOT NULL,
                    uploader_id INTEGER NOT NULL,
                    is_deleted BOOLEAN DEFAULT '0' NOT NULL,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT fk_files_uploader_id FOREIGN KEY(uploader_id) REFERENCES users (id),
                    CONSTRAINT pk_files PRIMARY KEY (id)
                )",
                "CREATE UNIQUE INDEX uq_files_relative_path ON files (relative_path)",
                "CREATE INDEX ix_files_uploader_created ON files (uploader_id, created_at)",
                @"CREATE TABLE artifacts (
                    id INTEGER NOT NULL,
                    title VARCHAR(200) NOT NULL,
                    summary VARCHAR(500) NOT NULL,
                    content_markdown TEXT NOT NULL,
                    author_id INTEGER NOT NULL,
                    status VARCHAR(32) DEFAULT 'DRAFT' NOT NULL,
                    published_at DATETIME,
                    archived_at DATETIME,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_artifacts_status CHECK (status IN ('DRAFT', 'PUBLISHED', 'ARCHIVED')),
                    CONSTRAINT fk_artifacts_author_id FOREIGN KEY(author_id) REFERENCES users (id),
                    CONSTRAINT pk_artifacts PRIMARY KEY (id)
                )",
                "CREATE INDEX ix_artifacts_status_published ON artifacts (status, published_at)",
                "CREATE INDEX ix_artifacts_author_updated ON artifacts (author_id, updated_at)",
                @"CREATE TABLE artifact_files (
                    id INTEGER NOT NULL,
                    artifact_id INTEGER NOT NULL,
                    file_id INTEGER NOT NULL,
                    sort_order INTEGER DEFAULT '0' NOT NULL,
                    CONSTRAINT fk_artifact_files_artifact_id FOREIGN KEY(artifact_id) REFERENCES artifacts (id) ON DELETE CASCADE,
                    CONSTRAINT fk_artifact_files_file_id FOREIGN KEY(file_id) REFERENCES files (id),
                    CONSTRAINT pk_artifact_files PRIMARY KEY (id),
                    CONSTRAINT uq_artifact_files_artifact_file UNIQUE (artifact_id, file_id)
                )",
                "CREATE INDEX ix_artifact_files_file_id ON artifact_files (file_id)",
                @"CREATE TABLE comments (
                    id INTEGER NOT NULL,
                    artifact_id INTEGER NOT NULL,
                    author_id INTEGER NOT NULL,
                    content TEXT NOT NULL,
                    status VARCHAR(16) DEFAULT 'VISIBLE' NOT NULL,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_comments_status CHECK (status IN ('VISIBLE', 'HIDDEN')),
                    CONSTRAINT fk_comments_artifact_id FOREIGN KEY(artifact_id) REFERENCES artifacts (id) ON DELETE CASCADE,
                    CONSTRAINT fk_comments_author_id FOREIGN KEY(author_id) REFERENCES users (id),
                    CONSTRAINT pk_comments PRIMARY KEY (id)
                )",
                "CREATE INDEX ix_comments_artifact_created ON comments (artifact_id, created_at)",
            }),
        new(
            "20260819_0004",
            "20260819_0003",
            new[]
            {
                @"CREATE TABLE tasks (
                    id INTEGER NOT NULL,
                    title VARCHAR(200) NOT NULL,
                    description TEXT NOT NULL,
                    creator_id INTEGER NOT NULL,
                    status VARCHAR(32) DEFAULT 'OPEN' NOT NULL,
                    deadline_at DATETIME,
                    completed_at DATETIME,
                    closed_at DATETIME,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_tasks_status CHECK (status IN ('OPEN', 'COMPLETED', 'CLOSED')),
                    CONSTRAINT fk_tasks_creator_id FOREIGN KEY(creator_id) REFERENCES users (id),
                    CONSTRAINT pk_tasks PRIMARY KEY (id)
                )",
                "CREATE INDEX ix_tasks_status_updated ON tasks (status, updated_at)",
                "CREATE INDEX ix_tasks_creator_updated ON tasks (creator_id, updated_at)",
                @"CREATE TABLE issues (
                    id INTEGER NOT NULL,
                    title VARCHAR(200) NOT NULL,
                    description TEXT NOT NULL,
                    author_id INTEGER NOT NULL,
                    status VARCHAR(16) DEFAULT 'OPEN' NOT NULL,
                    closed_at DATETIME,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_issues_status CHECK (status IN ('OPEN', 'CLOSED')),
                    CONSTRAINT fk_issues_author_id FOREIGN KEY(author_id) REFERENCES users (id),
                    CONSTRAINT pk_issues PRIMARY KEY (id)
                )",
                "CREATE INDEX ix_issues_status_updated ON issues (status, updated_at)",
                "CREATE INDEX ix_issues_author_updated ON issues (author_id, updated_at)",
                // SQLite cannot alter column nullability or add table-level CHECK
                // constraints in place, so comments is rebuilt with the final shape:
                // artifact_id becomes nullable, issue_id is added, and exactly one of
                // the two target columns must be non-null.
                "DROP INDEX ix_comments_artifact_created",
                @"CREATE TABLE comments_new (
                    id INTEGER NOT NULL,
                    artifact_id INTEGER,
                    issue_id INTEGER,
                    author_id INTEGER NOT NULL,
                    content TEXT NOT NULL,
                    status VARCHAR(16) DEFAULT 'VISIBLE' NOT NULL,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_comments_status CHECK (status IN ('VISIBLE', 'HIDDEN')),
                    CONSTRAINT ck_comments_target CHECK ((artifact_id IS NULL) <> (issue_id IS NULL)),
                    CONSTRAINT fk_comments_artifact_id FOREIGN KEY(artifact_id) REFERENCES artifacts (id) ON DELETE CASCADE,
                    CONSTRAINT fk_comments_issue_id FOREIGN KEY(issue_id) REFERENCES issues (id) ON DELETE CASCADE,
                    CONSTRAINT fk_comments_author_id FOREIGN KEY(author_id) REFERENCES users (id),
                    CONSTRAINT pk_comments PRIMARY KEY (id)
                )",
                "INSERT INTO comments_new (id, artifact_id, issue_id, author_id, content, status, created_at, updated_at) " +
                    "SELECT id, artifact_id, NULL, author_id, content, status, created_at, updated_at FROM comments",
                "DROP TABLE comments",
                "ALTER TABLE comments_new RENAME TO comments",
                "CREATE INDEX ix_comments_artifact_created ON comments (artifact_id, created_at)",
                "CREATE INDEX ix_comments_issue_created ON comments (issue_id, created_at)",
            }),
        new(
            "20260819_0005",
            "20260819_0004",
            new[]
            {
                @"CREATE TABLE competitions (
                    id INTEGER NOT NULL,
                    title VARCHAR(200) NOT NULL,
                    summary VARCHAR(500) NOT NULL,
                    rules_markdown TEXT NOT NULL,
                    start_at DATETIME NOT NULL,
                    end_at DATETIME NOT NULL,
                    created_by INTEGER NOT NULL,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_competitions_window CHECK (start_at < end_at),
                    CONSTRAINT fk_competitions_created_by FOREIGN KEY(created_by) REFERENCES users (id),
                    CONSTRAINT pk_competitions PRIMARY KEY (id)
                )",
                "CREATE INDEX ix_competitions_start_end ON competitions (start_at, end_at)",
            }),
        new(
            "20260819_0006",
            "20260819_0005",
            new[]
            {
                // SQLite cannot replace a CHECK constraint in place, so tasks is
                // rebuilt with the extended closure status list; existing row
                // values stay valid.
                "DROP INDEX ix_tasks_status_updated",
                "DROP INDEX ix_tasks_creator_updated",
                @"CREATE TABLE tasks_new (
                    id INTEGER NOT NULL,
                    title VARCHAR(200) NOT NULL,
                    description TEXT NOT NULL,
                    creator_id INTEGER NOT NULL,
                    status VARCHAR(32) DEFAULT 'OPEN' NOT NULL,
                    deadline_at DATETIME,
                    completed_at DATETIME,
                    closed_at DATETIME,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_tasks_status CHECK (status IN ('OPEN', 'IN_PROGRESS', 'REVIEWING', 'COMPLETED', 'CLOSED')),
                    CONSTRAINT fk_tasks_new_creator_id FOREIGN KEY(creator_id) REFERENCES users (id),
                    CONSTRAINT pk_tasks_new PRIMARY KEY (id)
                )",
                "INSERT INTO tasks_new (id, title, description, creator_id, status, deadline_at, completed_at, closed_at, created_at, updated_at) "
                    + "SELECT id, title, description, creator_id, status, deadline_at, completed_at, closed_at, created_at, updated_at FROM tasks",
                "DROP TABLE tasks",
                "ALTER TABLE tasks_new RENAME TO tasks",
                "CREATE INDEX ix_tasks_status_updated ON tasks (status, updated_at)",
                "CREATE INDEX ix_tasks_creator_updated ON tasks (creator_id, updated_at)",
                @"CREATE TABLE task_participants (
                    id INTEGER NOT NULL,
                    task_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    status VARCHAR(32) DEFAULT 'ACTIVE' NOT NULL,
                    joined_at DATETIME NOT NULL,
                    left_at DATETIME,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_task_participants_status CHECK (status IN ('ACTIVE', 'LEFT')),
                    CONSTRAINT fk_task_participants_task_id FOREIGN KEY(task_id) REFERENCES tasks (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_task_participants_user_id FOREIGN KEY(user_id) REFERENCES users (id) ON DELETE RESTRICT,
                    CONSTRAINT pk_task_participants PRIMARY KEY (id),
                    CONSTRAINT uq_task_participants_task_user UNIQUE (task_id, user_id)
                )",
                "CREATE INDEX ix_task_participants_user ON task_participants (user_id, status)",
                @"CREATE TABLE task_submissions (
                    id INTEGER NOT NULL,
                    task_id INTEGER NOT NULL,
                    participant_id INTEGER NOT NULL,
                    artifact_id INTEGER NOT NULL,
                    round_no INTEGER NOT NULL,
                    note TEXT,
                    status VARCHAR(32) DEFAULT 'SUBMITTED' NOT NULL,
                    is_current BOOLEAN DEFAULT 1 NOT NULL,
                    submitted_at DATETIME NOT NULL,
                    revision_requested_at DATETIME,
                    decided_at DATETIME,
                    decider_id INTEGER,
                    decision_note TEXT,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_task_submissions_status CHECK (status IN ('SUBMITTED', 'REVISION_REQUIRED', 'ACCEPTED', 'REJECTED')),
                    CONSTRAINT ck_task_submissions_round CHECK (round_no >= 1),
                    CONSTRAINT fk_task_submissions_task_id FOREIGN KEY(task_id) REFERENCES tasks (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_task_submissions_participant_id FOREIGN KEY(participant_id) REFERENCES task_participants (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_task_submissions_artifact_id FOREIGN KEY(artifact_id) REFERENCES artifacts (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_task_submissions_decider_id FOREIGN KEY(decider_id) REFERENCES users (id) ON DELETE RESTRICT,
                    CONSTRAINT pk_task_submissions PRIMARY KEY (id),
                    CONSTRAINT uq_task_submissions_round UNIQUE (participant_id, round_no)
                )",
                "CREATE INDEX ix_task_submissions_task_status ON task_submissions (task_id, status)",
                "CREATE INDEX ix_task_submissions_participant ON task_submissions (participant_id)",
                "CREATE INDEX ix_task_submissions_artifact ON task_submissions (artifact_id)",
            }),
        new(
            "20260819_0007",
            "20260819_0006",
            new[]
            {
                // tasks is referenced by task_participants/task_submissions, so
                // the rebuild below needs foreign keys disabled.
                "PRAGMA foreign_keys = OFF",
                "DROP INDEX ix_tasks_status_updated",
                "DROP INDEX ix_tasks_creator_updated",
                @"CREATE TABLE tasks_new (
                    id INTEGER NOT NULL,
                    title VARCHAR(200) NOT NULL,
                    description TEXT NOT NULL,
                    creator_id INTEGER NOT NULL,
                    status VARCHAR(32) DEFAULT 'OPEN' NOT NULL,
                    competition_id INTEGER,
                    competition_required BOOLEAN,
                    competition_sort_order INTEGER,
                    competition_max_score NUMERIC(6, 2),
                    competition_weight NUMERIC(5, 2),
                    deadline_at DATETIME,
                    completed_at DATETIME,
                    closed_at DATETIME,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_tasks_status CHECK (status IN ('OPEN', 'IN_PROGRESS', 'REVIEWING', 'COMPLETED', 'CLOSED')),
                    CONSTRAINT fk_tasks_new_creator_id FOREIGN KEY(creator_id) REFERENCES users (id),
                    CONSTRAINT pk_tasks_new PRIMARY KEY (id),
                    CONSTRAINT ck_tasks_competition_score CHECK (competition_id IS NULL OR (competition_max_score > 0 AND competition_weight > 0)),
                    CONSTRAINT ck_tasks_competition_fields CHECK (competition_id IS NOT NULL OR (competition_required IS NULL AND competition_sort_order IS NULL AND competition_max_score IS NULL AND competition_weight IS NULL)),
                    CONSTRAINT fk_tasks_new_competition_id FOREIGN KEY(competition_id) REFERENCES competitions (id) ON DELETE RESTRICT
                )",
                "INSERT INTO tasks_new (id, title, description, creator_id, status, deadline_at, completed_at, closed_at, created_at, updated_at, competition_id, competition_required, competition_sort_order, competition_max_score, competition_weight) "
                    + "SELECT id, title, description, creator_id, status, deadline_at, completed_at, closed_at, created_at, updated_at, NULL, NULL, NULL, NULL, NULL FROM tasks",
                "DROP TABLE tasks",
                "ALTER TABLE tasks_new RENAME TO tasks",
                "CREATE INDEX ix_tasks_status_updated ON tasks (status, updated_at)",
                "CREATE INDEX ix_tasks_creator_updated ON tasks (creator_id, updated_at)",
                "CREATE INDEX ix_tasks_competition ON tasks (competition_id, competition_sort_order)",
                // SQLite cannot ALTER ADD a named table constraint; a column-level
                // CHECK inside ADD COLUMN enforces the same value set.
                "ALTER TABLE competitions ADD COLUMN status VARCHAR(32) NOT NULL DEFAULT 'PUBLISHED' "
                    + "CHECK (status IN ('DRAFT', 'PUBLISHED', 'RESULT_PUBLISHED', 'ARCHIVED'))",
                @"CREATE TABLE competition_registrations (
                    id INTEGER NOT NULL,
                    competition_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    status VARCHAR(32) DEFAULT 'REGISTERED' NOT NULL,
                    registered_at DATETIME NOT NULL,
                    cancelled_at DATETIME,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_competition_registrations_status CHECK (status IN ('REGISTERED', 'CANCELLED')),
                    CONSTRAINT fk_competition_registrations_competition_id FOREIGN KEY(competition_id) REFERENCES competitions (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_competition_registrations_user_id FOREIGN KEY(user_id) REFERENCES users (id) ON DELETE RESTRICT,
                    CONSTRAINT pk_competition_registrations PRIMARY KEY (id),
                    CONSTRAINT uq_competition_registrations_competition_user UNIQUE (competition_id, user_id)
                )",
                "CREATE INDEX ix_competition_registrations_user ON competition_registrations (user_id)",
                @"CREATE TABLE competition_reviews (
                    id INTEGER NOT NULL,
                    task_submission_id INTEGER NOT NULL,
                    reviewer_id INTEGER NOT NULL,
                    raw_score NUMERIC(6, 2) NOT NULL,
                    comment TEXT,
                    reviewed_at DATETIME NOT NULL,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT fk_competition_reviews_task_submission_id FOREIGN KEY(task_submission_id) REFERENCES task_submissions (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_competition_reviews_reviewer_id FOREIGN KEY(reviewer_id) REFERENCES users (id) ON DELETE RESTRICT,
                    CONSTRAINT pk_competition_reviews PRIMARY KEY (id),
                    CONSTRAINT uq_competition_reviews_submission UNIQUE (task_submission_id)
                )",
                @"CREATE TABLE competition_results (
                    id INTEGER NOT NULL,
                    competition_id INTEGER NOT NULL,
                    registration_id INTEGER NOT NULL,
                    total_score NUMERIC(7, 4) NOT NULL,
                    rank INTEGER NOT NULL,
                    award VARCHAR(200),
                    published_by INTEGER NOT NULL,
                    published_at DATETIME NOT NULL,
                    created_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    updated_at DATETIME DEFAULT (CURRENT_TIMESTAMP) NOT NULL,
                    CONSTRAINT ck_competition_results_rank CHECK (rank >= 1),
                    CONSTRAINT fk_competition_results_competition_id FOREIGN KEY(competition_id) REFERENCES competitions (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_competition_results_registration_id FOREIGN KEY(registration_id) REFERENCES competition_registrations (id) ON DELETE RESTRICT,
                    CONSTRAINT fk_competition_results_published_by FOREIGN KEY(published_by) REFERENCES users (id) ON DELETE RESTRICT,
                    CONSTRAINT pk_competition_results PRIMARY KEY (id),
                    CONSTRAINT uq_competition_results_competition_registration UNIQUE (competition_id, registration_id)
                )",
                "CREATE INDEX ix_competition_results_rank ON competition_results (competition_id, rank)",
                "PRAGMA foreign_keys = ON",
            }),
        new(
            "20260819_0008",
            "20260819_0007",
            new[]
            {
                @"INSERT INTO task_participants
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
                    AND participants.id IS NULL",
                @"UPDATE task_participants
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
                    )",
                @"UPDATE task_participants
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
                    )",
            }),
    };

    /// <summary>Apply every pending revision up to the head revision.</summary>
    public static string UpgradeToHead(string databasePath)
    {
        if (databasePath is ":memory:")
        {
            throw new NotSupportedException("In-memory databases cannot be migrated.");
        }
        var fullPath = Path.GetFullPath(databasePath);
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
        using var connection = Database.OpenSqlite(fullPath);
        Execute(connection, "PRAGMA foreign_keys = ON");
        Execute(connection, "PRAGMA journal_mode = WAL");

        Execute(
            connection,
            @"CREATE TABLE IF NOT EXISTS alembic_version (
                version_num VARCHAR(32) NOT NULL,
                CONSTRAINT alembic_version_pkc PRIMARY KEY (version_num)
            )");
        var current = ReadCurrentVersion(connection);
        if (current is not null && !Revisions.Any(revision => revision.Id == current))
        {
            throw new InvalidOperationException(
                $"Database revision '{current}' was not produced by this application's migrations.");
        }

        while (true)
        {
            var next = Revisions.FirstOrDefault(revision => revision.DownRevision == current);
            if (next is null)
            {
                break;
            }
            foreach (var statement in next.Statements)
            {
                Execute(connection, statement);
            }
            using var transaction = connection.BeginTransaction();
            Execute(connection, "DELETE FROM alembic_version", transaction);
            using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO alembic_version (version_num) VALUES ($version)";
                insert.Parameters.AddWithValue("$version", next.Id);
                insert.ExecuteNonQuery();
            }
            transaction.Commit();
            current = next.Id;
        }
        return current ?? Revisions[0].Id;
    }

    private static string? ReadCurrentVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version_num FROM alembic_version LIMIT 1";
        return command.ExecuteScalar() as string;
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
