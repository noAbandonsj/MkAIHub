using System.Text.Json;

namespace MkAIHub.Api.Core;

/// <summary>
/// One pydantic-style validation error entry (input and ctx are never echoed).
/// </summary>
public sealed class ValidationErrorDetail
{
    public ValidationErrorDetail(string type, IReadOnlyList<object> loc, string msg)
    {
        Type = type;
        Loc = loc;
        Msg = msg;
    }

    public string Type { get; }

    public IReadOnlyList<object> Loc { get; }

    public string Msg { get; }
}

/// <summary>
/// Raised for request validation failures; rendered as a 422
/// VALIDATION_ERROR body without echoing submitted values.
/// </summary>
public sealed class ApiValidationException : AppError
{
    public ApiValidationException(IReadOnlyList<ValidationErrorDetail> details)
        : base("VALIDATION_ERROR", "Request validation failed", 422, details)
    {
    }
}

/// <summary>A parsed JSON object body with case-sensitive field access.</summary>
public sealed class JsonObjectView
{
    private readonly Dictionary<string, JsonElement> _properties;

    public JsonObjectView(JsonDocument document)
    {
        _properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            _properties[property.Name] = property.Value.Clone();
        }
    }

    public IReadOnlyCollection<string> Keys => _properties.Keys;

    public bool Has(string name) => _properties.ContainsKey(name);

    public JsonElement? Get(string name)
        => _properties.TryGetValue(name, out var value) ? value : null;
}

public static class JsonBody
{
    public static async Task<JsonObjectView> ReadAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new ApiValidationException(new[]
            {
                new ValidationErrorDetail("json_invalid", new object[] { "body" }, exception.Message),
            });
        }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ApiValidationException(new[]
                {
                    new ValidationErrorDetail(
                        "model_type",
                        new object[] { "body" },
                        "Input should be a valid dictionary or object to extract fields from"),
                });
            }
            return new JsonObjectView(document);
        }
    }

    /// <summary>
    /// Read a request body that may be absent entirely (FastAPI "X | None = None"
    /// payloads); an empty body yields null instead of a parse error.
    /// </summary>
    public static async Task<JsonObjectView?> TryReadAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        var text = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new ApiValidationException(new[]
            {
                new ValidationErrorDetail("json_invalid", new object[] { "body" }, exception.Message),
            });
        }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ApiValidationException(new[]
                {
                    new ValidationErrorDetail(
                        "model_type",
                        new object[] { "body" },
                        "Input should be a valid dictionary or object to extract fields from"),
                });
            }
            return new JsonObjectView(document);
        }
    }
}

/// <summary>
/// Field reader that accumulates pydantic-compatible validation errors instead
/// of failing on the first problem.
/// </summary>
public sealed class FieldReader
{
    private readonly JsonObjectView _body;
    private readonly List<ValidationErrorDetail> _errors = new();

    public FieldReader(JsonObjectView body)
    {
        _body = body;
    }

    public bool HasErrors => _errors.Count > 0;

    public bool HasField(string field) => _body.Has(field);

    public ApiValidationException ToException() => new(_errors);

    public void Add(string type, string field, string msg)
        => _errors.Add(new ValidationErrorDetail(type, new object[] { "body", field }, msg));

    public void Add(string type, IReadOnlyList<object> loc, string msg)
        => _errors.Add(new ValidationErrorDetail(type, loc, msg));

    private void AddMissing(string field)
        => Add("missing", field, "Field required");

    /// <summary>
    /// Required string whose validator runs in pydantic "before" mode: any
    /// non-string input produces a custom "must be a string" value error.
    /// </summary>
    public string? RequiredStringBefore(string field, string displayName)
    {
        if (!_body.Has(field))
        {
            AddMissing(field);
            return null;
        }
        return StringBefore(field, displayName);
    }

    private string? StringBefore(string field, string displayName)
    {
        var element = _body.Get(field);
        if (element is null || element.Value.ValueKind == JsonValueKind.Null)
        {
            Add("value_error", field, $"Value error, {displayName} must be a string");
            return null;
        }
        if (element.Value.ValueKind != JsonValueKind.String)
        {
            Add("value_error", field, $"Value error, {displayName} must be a string");
            return null;
        }
        return element.Value.GetString()!;
    }

    /// <summary>Required string typed before an "after" validator runs.</summary>
    public string? RequiredString(string field)
    {
        if (!_body.Has(field))
        {
            AddMissing(field);
            return null;
        }
        var element = _body.Get(field)!.Value;
        if (element.ValueKind != JsonValueKind.String)
        {
            Add("string_type", field, "Input should be a valid string");
            return null;
        }
        return element.GetString()!;
    }

    /// <summary>
    /// Optional string (JSON null accepted, distinct from absence);
    /// non-string non-null input is a type error.
    /// </summary>
    public string? OptionalString(string field)
    {
        if (!_body.Has(field))
        {
            return null;
        }
        var element = _body.Get(field)!.Value;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (element.ValueKind != JsonValueKind.String)
        {
            Add("string_type", field, "Input should be a valid string");
            return null;
        }
        return element.GetString()!;
    }

    /// <summary>Optional "before" string with a custom non-string message.</summary>
    public string? OptionalStringBefore(string field, string displayName)
    {
        if (!_body.Has(field))
        {
            return null;
        }
        var element = _body.Get(field)!.Value;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (element.ValueKind != JsonValueKind.String)
        {
            Add("value_error", field, $"Value error, {displayName} must be a string");
            return null;
        }
        return element.GetString()!;
    }

    public int RequiredInt(string field)
    {
        if (!_body.Has(field))
        {
            AddMissing(field);
            return 0;
        }
        if (!TryReadInt(_body.Get(field)!.Value, out var value))
        {
            return 0;
        }
        return value;
    }

    /// <summary>Optional int (JSON null accepted); used for PATCH-style fields.</summary>
    public int? OptionalInt(string field)
    {
        if (!_body.Has(field))
        {
            return null;
        }
        var element = _body.Get(field)!.Value;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return TryReadInt(element, out var value, field) ? value : null;
    }

    /// <summary>Required bool using the lax pydantic bool coercion rules.</summary>
    public bool? RequiredBool(string field)
    {
        if (!_body.Has(field))
        {
            AddMissing(field);
            return null;
        }
        return OptionalBool(field);
    }

    /// <summary>Optional list of ints (JSON null accepted when nullable).</summary>
    public List<int>? OptionalIntList(string field, bool allowNull, int? maxLength = null)
    {
        if (!_body.Has(field))
        {
            return null;
        }
        var element = _body.Get(field)!.Value;
        if (element.ValueKind == JsonValueKind.Null)
        {
            if (allowNull)
            {
                return null;
            }
            Add("list_type", field, "Input should be a valid list");
            return null;
        }
        if (element.ValueKind != JsonValueKind.Array)
        {
            Add("list_type", field, "Input should be a valid list");
            return null;
        }
        var values = new List<int>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (!TryReadInt(item, out var value, field, index))
            {
                return null;
            }
            values.Add(value);
            index += 1;
        }
        if (maxLength is not null && values.Count > maxLength)
        {
            Add("too_long", field, $"List should have at most {maxLength} items");
        }
        return values;
    }

    public bool? OptionalBool(string field)
    {
        if (!_body.Has(field))
        {
            return null;
        }
        var element = _body.Get(field)!.Value;
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
                return element.GetString()!.ToLowerInvariant() switch
                {
                    "true" or "1" or "on" or "yes" => true,
                    "false" or "0" or "off" or "no" => false,
                    _ => BoolTypeError(field),
                };
            case JsonValueKind.Number:
                return element.GetDouble() switch
                {
                    1 => true,
                    0 => false,
                    _ => BoolTypeError(field),
                };
            default:
                return BoolTypeError(field);
        }
    }

    private bool? BoolTypeError(string field)
    {
        Add("bool_type", field, "Input should be a valid boolean");
        return null;
    }

    public string? OptionalEnum(string field, IReadOnlyCollection<string> allowed)
    {
        if (!_body.Has(field))
        {
            return null;
        }
        var element = _body.Get(field)!.Value;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (element.ValueKind != JsonValueKind.String)
        {
            Add("enum", field, $"Input should be {QuoteList(allowed)}");
            return null;
        }
        var value = element.GetString()!;
        if (!allowed.Contains(value))
        {
            Add("enum", field, $"Input should be {QuoteList(allowed)}");
            return null;
        }
        return value;
    }

    public string RequiredEnum(string field, IReadOnlyCollection<string> allowed)
    {
        if (!_body.Has(field))
        {
            AddMissing(field);
            return string.Empty;
        }
        var value = OptionalEnum(field, allowed);
        return value ?? string.Empty;
    }

    /// <summary>
    /// Required decimal matching pydantic's lax Decimal parsing: JSON numbers
    /// or numeric strings are accepted.
    /// </summary>
    public decimal? RequiredDecimal(
        string field,
        int? maxDigits = null,
        int? decimalPlaces = null,
        decimal? greaterThan = null,
        decimal? greaterThanEqual = null)
    {
        if (!_body.Has(field))
        {
            AddMissing(field);
            return null;
        }
        return OptionalDecimal(field, maxDigits, decimalPlaces, greaterThan, greaterThanEqual);
    }

    /// <summary>Optional decimal (JSON null accepted) with pydantic constraint errors.</summary>
    public decimal? OptionalDecimal(
        string field,
        int? maxDigits = null,
        int? decimalPlaces = null,
        decimal? greaterThan = null,
        decimal? greaterThanEqual = null)
    {
        if (!_body.Has(field))
        {
            return null;
        }
        var element = _body.Get(field)!.Value;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        decimal value;
        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString()!.Trim();
            if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                Add("decimal_parsing", field, "Input should be a valid decimal");
                return null;
            }
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            try
            {
                value = element.GetDecimal();
            }
            catch (FormatException)
            {
                Add("decimal_parsing", field, "Input should be a valid decimal");
                return null;
            }
            catch (OverflowException)
            {
                Add("decimal_parsing", field, "Input should be a valid decimal");
                return null;
            }
        }
        else
        {
            Add("decimal_parsing", field, "Input should be a valid decimal");
            return null;
        }
        if (!DecimalDigitsFits(value, maxDigits, decimalPlaces, out var digitCount, out var places))
        {
            if (maxDigits is not null && digitCount > maxDigits)
            {
                Add("decimal_max_digits", field, $"Decimal input should have no more than {maxDigits} digits in total");
                return null;
            }
            Add("decimal_max_places", field, $"Decimal input should have no more than {decimalPlaces} decimal places");
            return null;
        }
        if (greaterThan is not null && value <= greaterThan)
        {
            Add("greater_than", field, $"Input should be greater than {greaterThan}");
            return null;
        }
        if (greaterThanEqual is not null && value < greaterThanEqual)
        {
            Add("greater_than_equal", field, $"Input should be greater than or equal to {greaterThanEqual}");
            return null;
        }
        return value;
    }

    /// <summary>
    /// Count significant digits the way pydantic does: leading zeros in the
    /// integer part and trailing zeros in the fraction never count.
    /// </summary>
    private static bool DecimalDigitsFits(decimal value, int? maxDigits, int? decimalPlaces, out int digitCount, out int places)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var separator = text.IndexOf('.');
        var integerPart = separator < 0 ? text : text[..separator];
        var fractionPart = separator < 0 ? string.Empty : text[(separator + 1)..];
        integerPart = integerPart.TrimStart('0');
        fractionPart = fractionPart.TrimEnd('0');
        digitCount = integerPart.Length + fractionPart.Length;
        places = fractionPart.Length;
        return (maxDigits is null || digitCount <= maxDigits)
            && (decimalPlaces is null || places <= decimalPlaces);
    }

    /// <summary>
    /// Required datetime that must carry a UTC offset, normalized to UTC
    /// (pydantic datetime + "must include a UTC offset" validator).
    /// </summary>
    public DateTime? RequiredUtcDateTime(string field)
    {
        if (!_body.Has(field))
        {
            AddMissing(field);
            return null;
        }
        return OptionalUtcDateTime(field);
    }

    /// <summary>
    /// Optional datetime (JSON null accepted) with the same offset rules;
    /// a null return means "absent or explicitly null" to the caller.
    /// </summary>
    public DateTime? OptionalUtcDateTime(string field)
    {
        if (!_body.Has(field))
        {
            return null;
        }
        var element = _body.Get(field)!.Value;
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (!TryReadUtcDateTime(field, element, out var value))
        {
            return null;
        }
        if (value is null)
        {
            Add("value_error", field, "Value error, must include a UTC offset");
            return null;
        }
        return value;
    }

    /// <summary>
    /// Parse one JSON value as an offset-bearing datetime. Returns false for
    /// type/parse failures; a null value means the text was valid but naive.
    /// </summary>
    private bool TryReadUtcDateTime(string field, JsonElement element, out DateTime? value)
    {
        value = null;
        string? text = null;
        if (element.ValueKind == JsonValueKind.String)
        {
            text = element.GetString();
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            // pydantic accepts unix epoch seconds and yields an aware datetime.
            var seconds = element.GetDouble();
            var offset = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000));
            value = offset.UtcDateTime;
            return true;
        }
        else
        {
            Add("datetime_type", field, "Input should be a valid datetime");
            return false;
        }

        var styles = System.Globalization.DateTimeStyles.AllowTrailingWhite | System.Globalization.DateTimeStyles.AllowLeadingWhite;
        if (!DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, styles, out var parsed))
        {
            Add("datetime_parsing", field, "Input should be a valid datetime, unexpected format");
            return false;
        }
        if (parsed.Kind == DateTimeKind.Unspecified)
        {
            // Naive text: valid datetime shape but no offset supplied.
            value = null;
            return true;
        }
        if (!DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, styles, out var offsetParsed))
        {
            Add("datetime_parsing", field, "Input should be a valid datetime, unexpected format");
            return false;
        }
        value = offsetParsed.UtcDateTime;
        return true;
    }

    public void ForbidExtraFields(params string[] knownFields)
    {
        foreach (var key in _body.Keys)
        {
            if (Array.IndexOf(knownFields, key) < 0)
            {
                Add("extra_forbidden", key, "Extra inputs are not permitted");
            }
        }
    }

    private bool TryReadInt(JsonElement element, out int value, string? field = null, int index = -1)
    {
        value = 0;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetInt32(out value))
                {
                    return true;
                }
                if (field is not null)
                {
                    IntError(field, index);
                }
                return false;
            case JsonValueKind.String:
                if (int.TryParse(element.GetString(), out value))
                {
                    return true;
                }
                if (field is not null)
                {
                    IntError(field, index);
                }
                return false;
            default:
                if (field is not null)
                {
                    IntError(field, index);
                }
                return false;
        }
    }

    private void IntError(string field, int index)
    {
        var loc = index >= 0
            ? new object[] { "body", field, index }
            : new object[] { "body", field };
        Add("int_parsing", loc, "Input should be a valid integer, unable to parse string as an integer");
    }

    public static string QuoteList(IReadOnlyCollection<string> values)
        => string.Join(" or ", values.Select(value => $"'{value}'"));
}
