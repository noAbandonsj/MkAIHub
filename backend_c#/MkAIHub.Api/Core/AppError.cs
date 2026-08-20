using System.Text.Json.Serialization;

namespace MkAIHub.Api.Core;

/// <summary>
/// A safe, expected error that can be exposed to an API client.
/// Mirrors app.core.errors.AppError.
/// </summary>
public class AppError : Exception
{
    public string Code { get; }

    public int StatusCode { get; }

    public object? Details { get; }

    public AppError(string code, string message, int statusCode = 400, object? details = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details;
    }
}

/// <summary>
/// Uniform error body returned by all API exception handlers.
/// Mirrors app.core.errors.ErrorResponse (model_dump(exclude_none=True)).
/// </summary>
public sealed class ErrorResponse
{
    public string Code { get; }

    public string Message { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Details { get; }

    public ErrorResponse(string code, string message, object? details = null)
    {
        Code = code;
        Message = message;
        Details = details;
    }
}
