using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MkAIHub.Api.Core;
using Microsoft.Extensions.FileProviders;

namespace MkAIHub.Api.Api;

/// <summary>
/// Outermost middleware: renders uniform JSON error bodies for AppError /
/// unexpected exceptions and logs one structured access record per request.
/// Mirrors the FastAPI exception handlers and request_logging_middleware.
/// </summary>
public sealed class ApiErrorLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;

    public ApiErrorLoggingMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _next = next;
        _logger = loggerFactory.CreateLogger("mkaihub.http");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        catch (AppError exception)
        {
            await WriteErrorAsync(
                context,
                exception.StatusCode,
                exception.Code,
                exception.Message,
                exception.Details);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unhandled application error");
            await WriteErrorAsync(
                context,
                500,
                "INTERNAL_SERVER_ERROR",
                "Internal server error",
                null);
        }
        finally
        {
            var durationMs = Math.Round(
                (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency,
                2);
            _logger.LogHttpRequest(
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                durationMs);
        }
    }

    public static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        object? details)
    {
        if (context.Response.HasStarted)
        {
            return;
        }
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(ApiJson.Serialize(new ErrorResponse(code, message, details)));
    }
}

/// <summary>
/// Terminal middleware serving the SPA fallback for non-API paths (or the
/// uniform 404 body otherwise). Mirrors SPAStaticFiles plus the Starlette
/// HTTPException handler.
/// </summary>
public sealed class SpaTerminalMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _indexHtmlPath;

    public SpaTerminalMiddleware(RequestDelegate next, Settings settings)
    {
        _next = next;
        var dist = settings.FrontendDist;
        var index = Path.Combine(dist, "index.html");
        if (Directory.Exists(dist) && File.Exists(index))
        {
            _indexHtmlPath = index;
        }
    }

    public bool SpaEnabled => _indexHtmlPath is not null;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api"))
        {
            await ApiErrorLoggingMiddleware.WriteErrorAsync(
                context, 404, "NOT_FOUND", "API route not found", null);
            return;
        }
        var isGetOrHead = HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method);
        if (isGetOrHead && _indexHtmlPath is not null)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html";
            await using var file = File.OpenRead(_indexHtmlPath);
            await file.CopyToAsync(context.Response.Body);
            return;
        }
        await ApiErrorLoggingMiddleware.WriteErrorAsync(
            context, 404, "HTTP_ERROR", "Not Found", null);
    }
}

/// <summary>Shared controller helpers.</summary>
public static class ControllerHelpers
{
    /// <summary>Serialize a payload with the shared snake_case options.</summary>
    public static ContentResult SnakeJson(this ControllerBase controller, object? value, int statusCode = 200)
        => new()
        {
            Content = ApiJson.Serialize(value),
            ContentType = "application/json",
            StatusCode = statusCode,
        };
}

/// <summary>Query-parameter parsing with FastAPI-compatible validation errors.</summary>
public static class QueryParams
{
    public static int ParseInt(
        HttpRequest request,
        List<ValidationErrorDetail> errors,
        string name,
        int fallback,
        int? minimum = null,
        int? maximum = null)
    {
        if (!request.Query.TryGetValue(name, out var values))
        {
            return fallback;
        }
        var raw = values.ToString();
        if (!int.TryParse(raw, out var value))
        {
            errors.Add(new ValidationErrorDetail(
                "int_parsing",
                new object[] { "query", name },
                "Input should be a valid integer, unable to parse string as an integer"));
            return fallback;
        }
        if (minimum is not null && value < minimum)
        {
            errors.Add(new ValidationErrorDetail(
                "greater_than_equal",
                new object[] { "query", name },
                $"Input should be greater than or equal to {minimum}"));
        }
        if (maximum is not null && value > maximum)
        {
            errors.Add(new ValidationErrorDetail(
                "less_than_equal",
                new object[] { "query", name },
                $"Input should be less than or equal to {maximum}"));
        }
        return value;
    }

    /// <summary>Bounded search text (FastAPI str | None max_length).</summary>
    public static string? ParseSearch(
        HttpRequest request,
        List<ValidationErrorDetail> errors,
        string name,
        int maxLength)
    {
        if (!request.Query.TryGetValue(name, out var values))
        {
            return null;
        }
        var value = values.ToString();
        if (value.Length > maxLength)
        {
            errors.Add(new ValidationErrorDetail(
                "string_too_long",
                new object[] { "query", name },
                $"String should have at most {maxLength} characters"));
        }
        return value;
    }

    public static bool ParseBool(
        HttpRequest request,
        List<ValidationErrorDetail> errors,
        string name,
        bool fallback = false)
    {
        if (!request.Query.TryGetValue(name, out var values))
        {
            return fallback;
        }
        return values.ToString().ToLowerInvariant() switch
        {
            "true" or "1" or "on" or "yes" => true,
            "false" or "0" or "off" or "no" => false,
            _ => BoolError(errors, name, fallback),
        };
    }

    private static bool BoolError(List<ValidationErrorDetail> errors, string name, bool fallback)
    {
        errors.Add(new ValidationErrorDetail(
            "bool_parsing",
            new object[] { "query", name },
            "Input should be a valid boolean, unable to interpret input as boolean"));
        return fallback;
    }

    public static string? ParseEnum(
        HttpRequest request,
        List<ValidationErrorDetail> errors,
        string name,
        IReadOnlyCollection<string> allowed)
    {
        if (!request.Query.TryGetValue(name, out var values))
        {
            return null;
        }
        var value = values.ToString();
        if (!allowed.Contains(value))
        {
            errors.Add(new ValidationErrorDetail(
                "enum",
                new object[] { "query", name },
                $"Input should be {FieldReader.QuoteList(allowed)}"));
            return null;
        }
        return value;
    }

    /// <summary>Parse a path parameter as an int, adding a validation error otherwise.</summary>
    public static int ParsePathInt(
        RouteValueDictionary routeValues,
        List<ValidationErrorDetail> errors,
        string pythonName,
        string routeName)
    {
        var raw = Convert.ToString(routeValues[routeName]);
        if (int.TryParse(raw, out var value))
        {
            return value;
        }
        errors.Add(new ValidationErrorDetail(
            "int_parsing",
            new object[] { "path", pythonName },
            "Input should be a valid integer, unable to parse string as an integer"));
        return 0;
    }

    public static void ThrowIfErrors(List<ValidationErrorDetail> errors)
    {
        if (errors.Count > 0)
        {
            throw new ApiValidationException(errors);
        }
    }
}
