using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Security;

namespace MkAIHub.Api.Tests;

public sealed class ConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("too-short")]
    [InlineData(Settings.ExampleSecretValue)]
    public void ProductionRejectsMissingWeakOrExampleSecret(string? secret)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APP_ENV"] = "production",
                ["APP_SECRET_KEY"] = secret,
            })
            .Build();

        var exception = Assert.Throws<ArgumentException>(
            () => Settings.FromConfiguration(configuration));
        Assert.Contains("APP_SECRET_KEY must be replaced", exception.Message);
    }

    [Fact]
    public void ProductionAcceptsAReplacedSecret()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APP_ENV"] = "production",
                ["APP_SECRET_KEY"] = new string('x', 32),
            })
            .Build();

        var settings = Settings.FromConfiguration(configuration);
        Assert.Equal("production", settings.AppEnv);
    }
}

public sealed class DatabaseTests
{
    [Fact]
    public void SqliteConnectionPragmasAreApplied()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkaihub-csharp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "pragmas.db");
        try
        {
            Migrator.UpgradeToHead(databasePath);
            using var connection = Database.OpenSqlite(databasePath);
            Assert.Equal(1L, QueryLong(connection, "PRAGMA foreign_keys"));
            Assert.Equal("wal", QueryString(connection, "PRAGMA journal_mode").ToLowerInvariant());
            Assert.True(QueryLong(connection, "PRAGMA busy_timeout") >= Database.SqliteBusyTimeoutMs);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigratorAppliesThePythonSchemaAndStampsAlembicVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkaihub-csharp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "migration.db");
        try
        {
            Migrator.UpgradeToHead(databasePath);
            using var connection = Database.OpenSqlite(databasePath);

            var tables = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }
            Assert.Equal(
                new HashSet<string>
                {
                    "alembic_version",
                    "artifact_files",
                    "artifacts",
                    "comments",
                    "competition_registrations",
                    "competition_results",
                    "competition_reviews",
                    "competitions",
                    "files",
                    "issues",
                    "task_participants",
                    "task_submissions",
                    "tasks",
                    "user_sessions",
                    "users",
                },
                tables.ToHashSet());

            Assert.Equal(Migrator.Head, QueryString(connection, "SELECT version_num FROM alembic_version"));
            Assert.Equal("20260819_0008", Migrator.Head);

            // The closed-loop rebuild keeps the widened task status CHECK and the
            // competition lifecycle CHECK delivered by migrations 0006/0007.
            Assert.Contains(
                "status IN ('OPEN', 'IN_PROGRESS', 'REVIEWING', 'COMPLETED', 'CLOSED')",
                QueryString(
                    connection,
                    "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'tasks'"));
            Assert.Equal(
                1L,
                QueryLong(
                    connection,
                    "SELECT COUNT(*) FROM pragma_table_info('competitions') WHERE name = 'status'"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MigratorBackfillsCompetitionTaskParticipants()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkaihub-csharp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "migration-auto-participants.db");
        try
        {
            Migrator.UpgradeToHead(databasePath);
            using (var connection = Database.OpenSqlite(databasePath))
            {
                Execute(connection,
                    "INSERT INTO users (id, username, display_name, password_hash, role, is_active, created_at, updated_at) "
                    + "VALUES (101, 'registered', 'Registered', 'x', 'EMPLOYEE', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP), "
                    + "(102, 'orphan', 'Orphan', 'x', 'EMPLOYEE', 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)");
                Execute(connection,
                    "INSERT INTO competitions (id, title, summary, rules_markdown, start_at, end_at, status, created_by, created_at, updated_at) "
                    + "VALUES (201, '自动领取赛', 's', 'r', '2020-01-01 00:00:00', '2999-01-01 00:00:00', "
                    + "'PUBLISHED', 101, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)");
                Execute(connection,
                    "INSERT INTO tasks (id, title, description, creator_id, status, competition_id, competition_required, "
                    + "competition_sort_order, competition_max_score, competition_weight, created_at, updated_at) VALUES "
                    + "(301, '必做', 'd', 101, 'OPEN', 201, 1, 1, 30.00, 10.00, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP), "
                    + "(302, '选做', 'd', 101, 'OPEN', 201, 0, 2, 30.00, 10.00, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)");
                Execute(connection,
                    "INSERT INTO competition_registrations "
                    + "(id, competition_id, user_id, status, registered_at, created_at, updated_at) "
                    + "VALUES (401, 201, 101, 'REGISTERED', '2026-08-27 01:02:03', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)");
                Execute(connection,
                    "INSERT INTO task_participants "
                    + "(id, task_id, user_id, status, joined_at, left_at, created_at, updated_at) VALUES "
                    + "(501, 301, 101, 'LEFT', '2026-08-26 01:00:00', '2026-08-26 02:00:00', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP), "
                    + "(502, 301, 102, 'ACTIVE', CURRENT_TIMESTAMP, NULL, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)");
                Execute(connection, "UPDATE alembic_version SET version_num = '20260819_0007'");
            }

            Migrator.UpgradeToHead(databasePath);
            using var verified = Database.OpenSqlite(databasePath);
            Assert.Equal(
                2L,
                QueryLong(
                    verified,
                    "SELECT COUNT(*) FROM task_participants WHERE user_id = 101 AND status = 'ACTIVE' AND left_at IS NULL"));
            Assert.Equal(
                "LEFT",
                QueryString(verified, "SELECT status FROM task_participants WHERE id = 502"));
            Assert.Equal(Migrator.Head, QueryString(verified, "SELECT version_num FROM alembic_version"));
            Assert.Equal(0L, QueryLong(verified, "SELECT COUNT(*) FROM pragma_foreign_key_check"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Execute(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long QueryLong(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static string QueryString(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }
}

public sealed class CliTests
{
    [Fact]
    public void CreateAdminCliCreatesSystemAdministrator()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkaihub-csharp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "cli.db");
        try
        {
            Migrator.UpgradeToHead(databasePath);
            var exitCode = MkAIHub.Api.Cli.CliCommands.CreateAdmin(
                new[]
                {
                    "--database-url", $"sqlite:///{databasePath.Replace('\\', '/')}",
                    "--username", "Admin.User",
                    "--display-name", "Admin User",
                    "--password", "password1",
                },
                Console.In,
                Console.Error);
            Assert.Equal(0, exitCode);

            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(Database.ConnectionStringFor(databasePath))
                .Options;
            using var db = new AppDbContext(options);
            var user = db.Users.SingleOrDefault(item => item.Username == "admin.user");
            Assert.NotNull(user);
            Assert.Equal("SYSTEM_ADMIN", user!.Role);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BackupAndRestoreRoundTripTheDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkaihub-csharp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "source.db");
        var restoredPath = Path.Combine(root, "restored.db");
        var outputDir = Path.Combine(root, "backups");
        var uploadDir = Path.Combine(root, "uploads");
        try
        {
            Migrator.UpgradeToHead(sourcePath);
            Assert.Equal(0, MkAIHub.Api.Cli.CliCommands.CreateAdmin(
                new[]
                {
                    "--database-url", $"sqlite:///{sourcePath.Replace('\\', '/')}",
                    "--username", "backupadmin",
                    "--display-name", "Backup Admin",
                    "--password", "password1",
                },
                Console.In,
                Console.Error));

            // The CLI reads UPLOAD_DIR from the environment; point it at an
            // isolated directory holding one file so the zip path is exercised.
            Directory.CreateDirectory(Path.Combine(uploadDir, "2026", "08"));
            var uploadFile = Path.Combine(uploadDir, "2026", "08", "guide.md");
            File.WriteAllText(uploadFile, "# Internal guide");
            var previousUploadDir = Environment.GetEnvironmentVariable("UPLOAD_DIR");
            Environment.SetEnvironmentVariable("UPLOAD_DIR", uploadDir);
            try
            {
                Assert.Equal(0, MkAIHub.Api.Cli.CliCommands.RunBackup(
                    new[]
                    {
                        "--database-url", $"sqlite:///{sourcePath.Replace('\\', '/')}",
                        "--output-dir", outputDir,
                    },
                    Console.Error));
            }
            finally
            {
                Environment.SetEnvironmentVariable("UPLOAD_DIR", previousUploadDir);
            }
            var dbBackup = Directory.GetFiles(outputDir, "mkaihub-backup-*.db").Single();
            var uploadsBackup = Directory.GetFiles(outputDir, "mkaihub-backup-*-uploads.zip").Single();
            using (var archive = System.IO.Compression.ZipFile.OpenRead(uploadsBackup))
            {
                var entry = archive.Entries.Single();
                Assert.Equal("2026/08/guide.md", entry.FullName);
            }

            // Restore refuses to overwrite without an explicit confirmation.
            Assert.Equal(2, MkAIHub.Api.Cli.CliCommands.RunRestore(
                new[]
                {
                    "--database-url", $"sqlite:///{restoredPath.Replace('\\', '/')}",
                    "--db-file", dbBackup,
                },
                Console.Error));

            Assert.Equal(0, MkAIHub.Api.Cli.CliCommands.RunRestore(
                new[]
                {
                    "--database-url", $"sqlite:///{restoredPath.Replace('\\', '/')}",
                    "--db-file", dbBackup,
                    "--yes",
                },
                Console.Error));

            using (var connection = Database.OpenSqlite(restoredPath))
            {
                Assert.Equal(
                    Migrator.Head,
                    QueryString(connection, "SELECT version_num FROM alembic_version"));
            }
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(Database.ConnectionStringFor(restoredPath))
                .Options;
            using var db = new AppDbContext(options);
            Assert.NotNull(db.Users.SingleOrDefault(item => item.Username == "backupadmin"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RestoreRejectsACorruptBackupDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "mkaihub-csharp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var corruptPath = Path.Combine(root, "corrupt.db");
        var restoredPath = Path.Combine(root, "restored.db");
        try
        {
            File.WriteAllText(corruptPath, "this is definitely not a sqlite database");
            Assert.Equal(2, MkAIHub.Api.Cli.CliCommands.RunRestore(
                new[]
                {
                    "--database-url", $"sqlite:///{restoredPath.Replace('\\', '/')}",
                    "--db-file", corruptPath,
                    "--yes",
                },
                Console.Error));
            Assert.False(File.Exists(restoredPath));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static string QueryString(Microsoft.Data.Sqlite.SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }
}

public sealed class PasswordTests
{
    /// <summary>
    /// Hash produced by the Python backend's argon2-cffi hasher for
    /// "password1"; the C# implementation must verify it.
    /// </summary>
    private const string PythonGeneratedHash =
        "$argon2id$v=19$m=19456,t=2,p=2$wZu5g7mYaa6IcsT7rGQbXQ$wcaxC1TqsmP0IYbRuT45a6ynoz3zjDa4dcIZRA4fTAg";

    [Fact]
    public void VerifiesPythonGeneratedHash()
    {
        Assert.True(PasswordHasher.VerifyPassword("password1", PythonGeneratedHash));
        Assert.False(PasswordHasher.VerifyPassword("wrong-password", PythonGeneratedHash));
    }

    [Fact]
    public void GeneratedHashUsesTheSharedParameters()
    {
        var hash = PasswordHasher.HashPassword("password1");
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=2$", hash);
        Assert.True(PasswordHasher.VerifyPassword("password1", hash));
        Assert.False(PasswordHasher.VerifyPassword("wrong-password", hash));
    }

    [Fact]
    public void MalformedOrForeignHashesAreRejected()
    {
        Assert.False(PasswordHasher.VerifyPassword("password1", "not-a-hash"));
        Assert.False(PasswordHasher.VerifyPassword("password1", ""));
        Assert.False(PasswordHasher.VerifyPassword("password1", "$argon2i$v=19$m=1024,t=1,p=1$AAAA$BBBB"));
        Assert.False(PasswordHasher.VerifyPassword("password1", "$argon2id$v=16$m=1024,t=1,p=1$AAAA$BBBB"));
    }

    [Fact]
    public void SessionTokenHashIsLowercaseSha256Hex()
    {
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("x")));
        Assert.NotEqual(expected, PasswordHasher.HashSessionToken("x"));
        Assert.Matches("^[0-9a-f]{64}$", PasswordHasher.HashSessionToken("x"));
    }
}
