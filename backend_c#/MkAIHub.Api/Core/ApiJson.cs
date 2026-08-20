using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MkAIHub.Api.Core;

/// <summary>
/// snake_case naming policy matching the FastAPI/pydantic response contract.
/// </summary>
public sealed class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public static readonly SnakeCaseNamingPolicy Instance = new();

    public override string ConvertName(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index += 1)
        {
            var character = name[index];
            if (char.IsUpper(character))
            {
                if (index > 0)
                {
                    builder.Append('_');
                }
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }
        return builder.ToString();
    }

    public static string ToSnakeCase(string name) => Instance.ConvertName(name);
}

/// <summary>
/// Serializes DateTimes as UTC ISO-8601 with a trailing "Z", matching the
/// pydantic field serializer (isoformat().replace("+00:00", "Z")).
/// </summary>
public sealed class DateTimeUtcConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(FormatIso(value));

    public static string FormatIso(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        return FormatIsoUtc(utc, "Z");
    }

    public static string FormatIsoUtc(DateTime utc, string suffix)
    {
        var fraction = utc.Ticks % TimeSpan.TicksPerSecond;
        var truncatedMicros = fraction / 10;
        var datePart = utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        if (truncatedMicros == 0)
        {
            return datePart + suffix;
        }
        return string.Create(CultureInfo.InvariantCulture, $"{datePart}.{truncatedMicros:D6}{suffix}");
    }
}

public sealed class NullableDateTimeUtcConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(DateTimeUtcConverter.FormatIso(value.Value));
        }
    }
}

/// <summary>
/// Shared serializer options for every JSON response body.
/// </summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = SnakeCaseNamingPolicy.Instance,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new DateTimeUtcConverter(),
            new NullableDateTimeUtcConverter(),
        },
    };

    public static string Serialize(object? value) => JsonSerializer.Serialize(value, Options);
}
