using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MkAIHub.Api.Core;

/// <summary>
/// Formats each log record as one structured JSON object per line on stdout,
/// mirroring app.core.logging.JsonFormatter. Only the "mkaihub" logger
/// hierarchy emits output, matching the Python logger wiring.
/// </summary>
public sealed class JsonLoggerProvider : ILoggerProvider
{
    private readonly LogLevel _minimumLevel;

    public JsonLoggerProvider(LogLevel minimumLevel)
    {
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) => new JsonLogger(categoryName, _minimumLevel);

    public void Dispose()
    {
    }
}

public sealed class JsonLogger : ILogger
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _category;
    private readonly LogLevel _minimumLevel;

    public JsonLogger(string category, LogLevel minimumLevel)
    {
        _category = category;
        _minimumLevel = minimumLevel;
    }

    public bool IsEnabled(LogLevel logLevel)
        => _category.StartsWith("mkaihub", StringComparison.Ordinal)
            && logLevel >= _minimumLevel
            && logLevel != LogLevel.None;

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTimeUtcConverter.FormatIsoUtc(
                DateTime.UtcNow,
                "+00:00"),
            ["level"] = LevelName(logLevel),
            ["logger"] = _category,
            ["message"] = formatter(state, exception) ?? state?.ToString() ?? string.Empty,
        };
        if (exception is not null)
        {
            payload["exception"] = exception.ToString();
        }
        if (state is IReadOnlyList<KeyValuePair<string, object?>> fields)
        {
            foreach (var field in fields)
            {
                if (field.Key is "method" or "path" or "status_code" or "duration_ms"
                    && field.Value is not null)
                {
                    payload[field.Key] = field.Value;
                }
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(payload, Options));
    }

    private static string LevelName(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARNING",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _ => "INFO",
    };
}

public static class ApiLogging
{
    public static LogLevel ParseLevel(string level)
    {
        return level.Trim().ToUpperInvariant() switch
        {
            "TRACE" => LogLevel.Trace,
            "DEBUG" => LogLevel.Debug,
            "INFO" => LogLevel.Information,
            "WARNING" => LogLevel.Warning,
            "ERROR" => LogLevel.Error,
            "CRITICAL" => LogLevel.Critical,
            _ => LogLevel.Information,
        };
    }
}

public static class LoggerExtensions
{
    /// <summary>Log the per-request access record with the standard fields.</summary>
    public static void LogHttpRequest(
        this ILogger logger,
        string method,
        string path,
        int statusCode,
        double durationMs)
    {
        var state = new List<KeyValuePair<string, object?>>
        {
            new("method", method),
            new("path", path),
            new("status_code", statusCode),
            new("duration_ms", durationMs),
        };
        logger.Log(
            LogLevel.Information,
            default,
            state,
            null,
            static (_, _) => "HTTP request");
    }
}
