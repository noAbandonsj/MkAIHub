using Microsoft.Extensions.Configuration;

namespace MkAIHub.Api.Core;

/// <summary>
/// Environment-backed application settings loaded from environment variables
/// and the repository-root .env file. Mirrors app.core.config.Settings.
/// </summary>
public sealed class Settings
{
    public const string ExampleSecretValue = "replace-with-a-random-secret-of-at-least-32-characters";

    public string AppEnv { get; private set; } = "development";

    public string AppName { get; private set; } = "MkAIHub";

    public string? AppSecretKey { get; private set; }

    public string DatabaseUrl { get; private set; } = "sqlite:///./data/mkaihub.db";

    public string DataDir { get; private set; } = "./data";

    public string UploadDir { get; private set; } = "./storage/uploads";

    public int MaxUploadSizeMb { get; private set; } = 50;

    public int MaxAttachmentsPerArtifact { get; private set; } = 10;

    public string AllowedUploadExtensions { get; private set; } =
        "pdf,docx,xlsx,pptx,md,txt,csv,json,png,jpg,jpeg,webp,"
        + "py,js,ts,vue,sql,yaml,yml,toml,ipynb,zip";

    public string SessionCookieName { get; private set; } = "mkaihub_session";

    public int SessionTtlHours { get; private set; } = 24;

    public string? FrontendOrigin { get; private set; }

    public string LogLevel { get; private set; } = "INFO";

    public string? InitialAdminUsername { get; private set; }

    /// <summary>Normalized extensions for upload validation.</summary>
    public IReadOnlySet<string> AllowedExtensions =>
        new HashSet<string>(
            AllowedUploadExtensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(extension => extension.ToLowerInvariant().TrimStart('.').Trim()));

    /// <summary>Configured CORS origins, ignoring empty values.</summary>
    public IReadOnlyList<string> FrontendOrigins =>
        string.IsNullOrWhiteSpace(FrontendOrigin)
            ? Array.Empty<string>()
            : FrontendOrigin
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

    /// <summary>Repository root containing backend and frontend.</summary>
    public string ProjectRoot => FindRepositoryRoot();

    /// <summary>Optional production SPA directory (frontend/dist).</summary>
    public string FrontendDist => Path.Combine(ProjectRoot, "frontend", "dist");

    public bool IsProduction => string.Equals(AppEnv, "production", StringComparison.OrdinalIgnoreCase);

    public Settings WithDatabaseUrl(string databaseUrl)
    {
        var settings = Clone();
        settings.DatabaseUrl = databaseUrl;
        settings.Validate();
        return settings;
    }

    private Settings()
    {
    }

    private Settings Clone() => new()
    {
        AppEnv = AppEnv,
        AppName = AppName,
        AppSecretKey = AppSecretKey,
        DatabaseUrl = DatabaseUrl,
        DataDir = DataDir,
        UploadDir = UploadDir,
        MaxUploadSizeMb = MaxUploadSizeMb,
        MaxAttachmentsPerArtifact = MaxAttachmentsPerArtifact,
        AllowedUploadExtensions = AllowedUploadExtensions,
        SessionCookieName = SessionCookieName,
        SessionTtlHours = SessionTtlHours,
        FrontendOrigin = FrontendOrigin,
        LogLevel = LogLevel,
        InitialAdminUsername = InitialAdminUsername,
    };

    public static Settings FromConfiguration(IConfiguration configuration)
    {
        var settings = new Settings
        {
            AppEnv = configuration["APP_ENV"] ?? "development",
            AppName = configuration["APP_NAME"] ?? "MkAIHub",
            AppSecretKey = NullIfEmpty(configuration["APP_SECRET_KEY"]),
            DatabaseUrl = configuration["DATABASE_URL"] ?? "sqlite:///./data/mkaihub.db",
            DataDir = configuration["DATA_DIR"] ?? "./data",
            UploadDir = configuration["UPLOAD_DIR"] ?? "./storage/uploads",
            MaxUploadSizeMb = ParseIntSetting(configuration, "MAX_UPLOAD_SIZE_MB", 50),
            MaxAttachmentsPerArtifact = ParseIntSetting(configuration, "MAX_ATTACHMENTS_PER_ARTIFACT", 10),
            AllowedUploadExtensions = configuration["ALLOWED_UPLOAD_EXTENSIONS"]
                ?? "pdf,docx,xlsx,pptx,md,txt,csv,json,png,jpg,jpeg,webp,"
                + "py,js,ts,vue,sql,yaml,yml,toml,ipynb,zip",
            SessionCookieName = configuration["SESSION_COOKIE_NAME"] ?? "mkaihub_session",
            SessionTtlHours = ParseIntSetting(configuration, "SESSION_TTL_HOURS", 24),
            FrontendOrigin = NullIfEmpty(configuration["FRONTEND_ORIGIN"]),
            LogLevel = configuration["LOG_LEVEL"] ?? "INFO",
            InitialAdminUsername = NullIfEmpty(configuration["INITIAL_ADMIN_USERNAME"]),
        };
        settings.Validate();
        return settings;
    }

    /// <summary>Build settings from environment variables, optionally the .env file.</summary>
    public static Settings LoadFromEnvironment(bool includeDotEnvFile = true)
    {
        var builder = new ConfigurationBuilder();
        if (includeDotEnvFile)
        {
            // Real environment variables must override .env values, matching
            // the web host and pydantic-settings precedence.
            builder.AddDotEnvFile(Path.Combine(FindRepositoryRoot(), ".env"));
        }
        builder.AddEnvironmentVariables();
        return FromConfiguration(builder.Build());
    }

    private void Validate()
    {
        if (IsProduction)
        {
            var secretValue = AppSecretKey ?? string.Empty;
            if (secretValue.Length < 32 || secretValue == ExampleSecretValue)
            {
                throw new ArgumentException(
                    "APP_SECRET_KEY must be replaced with a random value of at least 32 characters "
                    + "in production");
            }
        }
        if (MaxUploadSizeMb <= 0)
        {
            throw new ArgumentException("MAX_UPLOAD_SIZE_MB must be greater than zero");
        }
        if (MaxAttachmentsPerArtifact <= 0)
        {
            throw new ArgumentException("MAX_ATTACHMENTS_PER_ARTIFACT must be greater than zero");
        }
        if (SessionTtlHours <= 0)
        {
            throw new ArgumentException("SESSION_TTL_HOURS must be greater than zero");
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static int ParseIntSetting(IConfiguration configuration, string key, int fallback)
    {
        var raw = configuration[key];
        if (string.IsNullOrEmpty(raw))
        {
            return fallback;
        }
        if (int.TryParse(raw, out var parsed))
        {
            return parsed;
        }
        throw new ArgumentException($"{key} must be an integer");
    }

    /// <summary>
    /// Locate the repository root (the directory containing backend/ and frontend/)
    /// by walking up from the application base directory.
    /// </summary>
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = directory; current != null; current = current.Parent)
        {
            var hasBackend = Directory.Exists(Path.Combine(current.FullName, "backend"))
                || Directory.Exists(Path.Combine(current.FullName, "backend_c#"));
            var hasFrontend = Directory.Exists(Path.Combine(current.FullName, "frontend"));
            if (hasBackend && hasFrontend)
            {
                return current.FullName;
            }
        }
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Convert a Python-style sqlite URL (or plain path) into a file path.
    /// </summary>
    public static string ResolveDatabasePath(string databaseUrl)
    {
        const string prefix = "sqlite:///";
        if (databaseUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return databaseUrl[prefix.Length..];
        }
        if (databaseUrl.StartsWith("sqlite://", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Only file-backed SQLite URLs are supported, got: {databaseUrl}");
        }
        return databaseUrl;
    }
}

public sealed class DotEnvConfigurationProvider : ConfigurationProvider
{
    private readonly string _path;

    public DotEnvConfigurationProvider(string path)
    {
        _path = path;
    }

    public override void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }
        foreach (var rawLine in File.ReadAllLines(_path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());
            Data[key] = value;
        }
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && ((value.StartsWith('"') && value.EndsWith('"'))
            || (value.StartsWith('\'') && value.EndsWith('\''))))
        {
            return value[1..^1];
        }
        return value;
    }
}

public sealed class DotEnvConfigurationSource : IConfigurationSource
{
    public string Path { get; init; } = string.Empty;

    public IConfigurationProvider Build(IConfigurationBuilder builder)
        => new DotEnvConfigurationProvider(Path);
}

public static class DotEnvConfigurationExtensions
{
    public static IConfigurationBuilder AddDotEnvFile(this IConfigurationBuilder builder, string path)
    {
        builder.Add(new DotEnvConfigurationSource { Path = path });
        return builder;
    }
}
