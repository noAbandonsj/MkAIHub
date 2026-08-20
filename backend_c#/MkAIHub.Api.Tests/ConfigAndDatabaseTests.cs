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
                    "files",
                    "user_sessions",
                    "users",
                },
                tables.ToHashSet());

            Assert.Equal("20260819_0003", QueryString(connection, "SELECT version_num FROM alembic_version"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
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
