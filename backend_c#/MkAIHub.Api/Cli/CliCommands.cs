using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Api;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Security;

namespace MkAIHub.Api.Cli;

/// <summary>
/// Operational command-line helpers, mirroring app.cli
/// (python -m app.cli create-admin / backup / restore) plus the alembic
/// upgrade equivalent.
/// </summary>
public static class CliCommands
{
    public static int Dispatch(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return -1;
        }
        switch (args[0])
        {
            case "create-admin":
                return CreateAdmin(args.Skip(1).ToList(), Console.In, Console.Error);
            case "migrate":
                return Migrate(args.Skip(1).ToList(), Console.Error);
            case "backup":
                return RunBackup(args.Skip(1).ToList(), Console.Error);
            case "restore":
                return RunRestore(args.Skip(1).ToList(), Console.Error);
            case "--help" or "-h" or "help":
                PrintUsage(Console.Out);
                return 0;
            default:
                Console.Error.WriteLine($"Unknown command: {args[0]}");
                PrintUsage(Console.Error);
                return 2;
        }
    }

    public static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: MkAIHub.Api [command]");
        writer.WriteLine();
        writer.WriteLine("Commands:");
        writer.WriteLine("  (none)          Start the HTTP API server (default port 8000).");
        writer.WriteLine("  migrate         Apply pending database migrations up to the head revision.");
        writer.WriteLine("  create-admin    Create the first system administrator.");
        writer.WriteLine("  backup          Back up the SQLite database and uploaded files.");
        writer.WriteLine("  restore         Restore the database and uploads from a backup.");
        writer.WriteLine();
        writer.WriteLine("create-admin options:");
        writer.WriteLine("  --username U        Username (3-64 chars, a-z0-9._-).");
        writer.WriteLine("  --display-name N    Display name (1-100 chars).");
        writer.WriteLine("  --password P        Password (8-128 chars); prefer the interactive prompt.");
        writer.WriteLine("  --database-url URL  sqlite:///... URL overriding DATABASE_URL.");
        writer.WriteLine();
        writer.WriteLine("backup options:");
        writer.WriteLine("  --database-url URL  sqlite:///... URL overriding DATABASE_URL.");
        writer.WriteLine("  --output-dir DIR    Directory for backup artifacts (default: <data_dir>/backups).");
        writer.WriteLine();
        writer.WriteLine("restore options:");
        writer.WriteLine("  --database-url URL  sqlite:///... URL overriding DATABASE_URL.");
        writer.WriteLine("  --db-file FILE      Backup database file created by `backup` (required).");
        writer.WriteLine("  --uploads-zip FILE  Backup uploads archive created by `backup`.");
        writer.WriteLine("  --yes               Confirm that the current database and uploads may be overwritten.");
    }

    private static Dictionary<string, string?> ParseOptions(IReadOnlyList<string> args)
    {
        var options = new Dictionary<string, string?>();
        for (var index = 0; index < args.Count; index += 1)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }
            string? value;
            if (argument.Contains('='))
            {
                var separator = argument.IndexOf('=');
                value = argument[(separator + 1)..];
                argument = argument[..separator];
            }
            else if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                index += 1;
                value = args[index];
            }
            else
            {
                value = null;
            }
            options[argument] = value;
        }
        return options;
    }

    public static int CreateAdmin(
        IReadOnlyList<string> args,
        TextReader input,
        TextWriter error)
    {
        var options = ParseOptions(args);
        var databaseUrl = options.GetValueOrDefault("--database-url");
        // Mirrors Settings(..., _env_file=None): .env is skipped once the URL
        // is overridden on the command line.
        var settings = Settings.LoadFromEnvironment(includeDotEnvFile: databaseUrl is null);
        if (databaseUrl is not null)
        {
            settings = settings.WithDatabaseUrl(databaseUrl);
        }

        var username = options.GetValueOrDefault("--username") ?? Prompt(input, "Username: ");
        var displayName = options.GetValueOrDefault("--display-name") ?? Prompt(input, "Display name: ");
        var password = options.GetValueOrDefault("--password");
        if (password is null)
        {
            password = ReadPassword("Password: ");
            var confirmation = ReadPassword("Confirm password: ");
            if (password != confirmation)
            {
                error.WriteLine("Passwords do not match");
                return 2;
            }
        }

        if (!AuthRules.TryNormalizeUsername(username ?? string.Empty, out var normalizedUsername, out var usernameError))
        {
            error.WriteLine($"Invalid administrator details: {usernameError}");
            return 2;
        }
        if (!AuthRules.TryNormalizeDisplayName(displayName ?? string.Empty, out var normalizedDisplay, out var displayError))
        {
            error.WriteLine($"Invalid administrator details: {displayError}");
            return 2;
        }
        if (!AuthRules.IsValidPassword(password!))
        {
            error.WriteLine("Invalid administrator details: Password must be between 8 and 128 characters");
            return 2;
        }

        var databasePath = Settings.ResolveDatabasePath(settings.DatabaseUrl);
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(Database.ConnectionStringFor(databasePath));
        using (var db = new AppDbContext(optionsBuilder.Options))
        {
            if (db.Users.Any(user => user.Username == normalizedUsername))
            {
                error.WriteLine("Username is already in use");
                return 2;
            }
            var now = DateTime.UtcNow;
            db.Users.Add(new User
            {
                Username = normalizedUsername,
                DisplayName = normalizedDisplay,
                PasswordHash = PasswordHasher.HashPassword(password!),
                Role = UserRoles.SystemAdmin,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            try
            {
                db.SaveChanges();
            }
            catch (DbUpdateException)
            {
                error.WriteLine("Username is already in use");
                return 2;
            }
        }
        Console.WriteLine($"Created system administrator: {normalizedUsername}");
        return 0;
    }

    public static int Migrate(IReadOnlyList<string> args, TextWriter error)
    {
        var options = ParseOptions(args);
        var databaseUrl = options.GetValueOrDefault("--database-url");
        var settings = Settings.LoadFromEnvironment(includeDotEnvFile: databaseUrl is null);
        if (databaseUrl is not null)
        {
            settings = settings.WithDatabaseUrl(databaseUrl);
        }
        try
        {
            var head = Migrator.UpgradeToHead(Settings.ResolveDatabasePath(settings.DatabaseUrl));
            Console.WriteLine($"Database is at revision {head}");
            return 0;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Migration failed: {exception.Message}");
            return 2;
        }
    }

    /// <summary>Create a consistent SQLite snapshot plus a zip of uploaded files.</summary>
    public static int RunBackup(IReadOnlyList<string> args, TextWriter error)
    {
        var options = ParseOptions(args);
        var databaseUrl = options.GetValueOrDefault("--database-url");
        var settings = Settings.LoadFromEnvironment(includeDotEnvFile: databaseUrl is null);
        if (databaseUrl is not null)
        {
            settings = settings.WithDatabaseUrl(databaseUrl);
        }

        var databasePath = ResolveFileDatabase(settings, error, out var failed);
        if (failed)
        {
            return 2;
        }
        if (!File.Exists(databasePath))
        {
            error.WriteLine($"Database file not found: {databasePath}");
            return 2;
        }

        var outputDir = options.GetValueOrDefault("--output-dir")
            ?? Path.Combine(settings.DataDir, "backups");
        Directory.CreateDirectory(outputDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        var dbTarget = Path.Combine(outputDir, $"mkaihub-backup-{stamp}.db");
        using (var source = Database.OpenSqlite(databasePath))
        using (var destination = new SqliteConnection(Database.ConnectionStringFor(dbTarget)))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }

        var uploadsTarget = Path.Combine(outputDir, $"mkaihub-backup-{stamp}-uploads.zip");
        var fileCount = 0;
        if (Directory.Exists(settings.UploadDir))
        {
            using var archive = ZipFile.Open(uploadsTarget, ZipArchiveMode.Create);
            foreach (var file in Directory
                .EnumerateFiles(settings.UploadDir, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var entryName = Path.GetRelativePath(settings.UploadDir, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                fileCount += 1;
            }
        }

        Console.WriteLine($"Database backup: {dbTarget}");
        Console.WriteLine($"Uploads backup: {uploadsTarget} ({fileCount} files)");
        return 0;
    }

    /// <summary>
    /// Overwrite the current database (and uploads) from a backup. The
    /// application must be stopped before restoring; active connections would
    /// keep writing to the replaced database via the old WAL files.
    /// </summary>
    public static int RunRestore(IReadOnlyList<string> args, TextWriter error)
    {
        var options = ParseOptions(args);
        var databaseUrl = options.GetValueOrDefault("--database-url");
        var settings = Settings.LoadFromEnvironment(includeDotEnvFile: databaseUrl is null);
        if (databaseUrl is not null)
        {
            settings = settings.WithDatabaseUrl(databaseUrl);
        }

        var dbFile = options.GetValueOrDefault("--db-file");
        if (dbFile is null)
        {
            error.WriteLine("--db-file is required");
            return 2;
        }
        if (!File.Exists(dbFile))
        {
            error.WriteLine($"Backup database not found: {dbFile}");
            return 2;
        }

        SqliteConnection.ClearAllPools();
        string integrity;
        try
        {
            using (var check = new SqliteConnection(Database.ConnectionStringFor(dbFile)))
            {
                check.Open();
                using var command = check.CreateCommand();
                command.CommandText = "PRAGMA integrity_check";
                integrity = command.ExecuteScalar() as string ?? string.Empty;
            }
        }
        catch (SqliteException)
        {
            // Not a readable SQLite database at all.
            integrity = string.Empty;
        }
        if (integrity != "ok")
        {
            error.WriteLine("Backup database failed the integrity check; refusing to restore");
            return 2;
        }

        if (!options.ContainsKey("--yes"))
        {
            error.WriteLine("Restore overwrites the current database and uploads. Re-run with --yes to confirm.");
            return 2;
        }

        var databasePath = ResolveFileDatabase(settings, error, out var failed);
        if (failed)
        {
            return 2;
        }
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }
        var parent = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }
        File.Copy(dbFile, databasePath, overwrite: true);

        var uploadsZip = options.GetValueOrDefault("--uploads-zip");
        if (uploadsZip is not null)
        {
            if (!File.Exists(uploadsZip))
            {
                error.WriteLine($"Uploads archive not found: {uploadsZip}");
                return 2;
            }
            Directory.CreateDirectory(settings.UploadDir);
            using var archive = ZipFile.OpenRead(uploadsZip);
            var restoredFiles = archive.Entries.Count;
            archive.ExtractToDirectory(settings.UploadDir, overwriteFiles: true);
            Console.WriteLine($"Database restored from: {dbFile}");
            Console.WriteLine($"Uploads restored from: {uploadsZip} ({restoredFiles} files)");
            return 0;
        }
        Console.WriteLine($"Database restored from: {dbFile}");
        return 0;
    }

    private static string ResolveFileDatabase(Settings settings, TextWriter error, out bool failed)
    {
        var path = Settings.ResolveDatabasePath(settings.DatabaseUrl);
        if (path is ":" or ":memory:")
        {
            error.WriteLine("Only file-backed SQLite databases can be backed up");
            failed = true;
            return string.Empty;
        }
        failed = false;
        return path;
    }

    private static string Prompt(TextReader input, string label)
    {
        Console.Write(label);
        return input.ReadLine() ?? string.Empty;
    }

    private static string ReadPassword(string label)
    {
        Console.Write(label);
        var builder = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length -= 1;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return builder.ToString();
    }
}
