using System.Text.RegularExpressions;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;

namespace MkAIHub.Api.Api;

public sealed record HealthResponseDto(string Status, string Service, string Environment, string Version);

public sealed record UserSummaryDto(int Id, string Username, string DisplayName);

public sealed record UserReadDto(
    int Id,
    string Username,
    string DisplayName,
    string Role,
    bool IsActive,
    DateTime? LastLoginAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CsrfTokenResponseDto(string CsrfToken);

public sealed record LoginResponseDto(UserReadDto User);

public sealed record UserListResponseDto(
    IReadOnlyList<UserReadDto> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record FileReadDto(
    int Id,
    string OriginalName,
    string Extension,
    string MimeType,
    int SizeBytes,
    int UploaderId,
    DateTime CreatedAt);

public sealed record ArtifactListItemDto(
    int Id,
    string Title,
    string Summary,
    UserSummaryDto Author,
    string Status,
    int AttachmentCount,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ArtifactReadDto(
    int Id,
    string Title,
    string Summary,
    UserSummaryDto Author,
    string Status,
    int AttachmentCount,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string ContentMarkdown,
    DateTime? ArchivedAt,
    IReadOnlyList<FileReadDto> Files);

public sealed record ArtifactListResponseDto(
    IReadOnlyList<ArtifactListItemDto> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record CommentReadDto(
    int Id,
    int? ArtifactId,
    int? IssueId,
    UserSummaryDto Author,
    string Content,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CommentListResponseDto(
    IReadOnlyList<CommentReadDto> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record ExploreResponseDto(IReadOnlyList<ArtifactListItemDto> LatestArtifacts);

public sealed record TaskListItemDto(
    int Id,
    string Title,
    UserSummaryDto Creator,
    string Status,
    DateTime? DeadlineAt,
    DateTime? CompletedAt,
    DateTime? ClosedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record TaskReadDto(
    int Id,
    string Title,
    UserSummaryDto Creator,
    string Status,
    DateTime? DeadlineAt,
    DateTime? CompletedAt,
    DateTime? ClosedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Description);

public sealed record TaskListResponseDto(
    IReadOnlyList<TaskListItemDto> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record IssueListItemDto(
    int Id,
    string Title,
    UserSummaryDto Author,
    string Status,
    DateTime? ClosedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record IssueReadDto(
    int Id,
    string Title,
    UserSummaryDto Author,
    string Status,
    DateTime? ClosedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string Description);

public sealed record IssueListResponseDto(
    IReadOnlyList<IssueListItemDto> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record CompetitionListItemDto(
    int Id,
    string Title,
    string Summary,
    UserSummaryDto Creator,
    string Status,
    DateTime StartAt,
    DateTime EndAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CompetitionReadDto(
    int Id,
    string Title,
    string Summary,
    UserSummaryDto Creator,
    string Status,
    DateTime StartAt,
    DateTime EndAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string RulesMarkdown);

public sealed record CompetitionListResponseDto(
    IReadOnlyList<CompetitionListItemDto> Items,
    int Page,
    int PageSize,
    int Total);

/// <summary>Shared normalization rules (app.schemas.auth helpers).</summary>
public static class AuthRules
{
    public static readonly Regex UsernamePattern = new(
        "^[a-z0-9._-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryNormalizeUsername(string value, out string normalized, out string error)
    {
        normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length is < 3 or > 64 || !UsernamePattern.IsMatch(normalized))
        {
            error = "Username must be 3-64 characters using letters, digits, '.', '_' or '-'";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeDisplayName(string value, out string normalized, out string error)
    {
        normalized = value.Trim();
        if (normalized.Length is < 1 or > 100)
        {
            error = "Display name must be 1-100 characters";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public static bool IsValidPassword(string value)
        => value.Length is >= 8 and <= 128;
}

public sealed record LoginRequest(string Username, string Password)
{
    public static async Task<LoginRequest> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var username = reader.RequiredStringBefore("username", "Username");
        var password = reader.RequiredString("password");
        if (username is not null)
        {
            if (!AuthRules.TryNormalizeUsername(username, out var normalized, out var error))
            {
                reader.Add("value_error", "username", $"Value error, {error}");
                username = null;
            }
            else
            {
                username = normalized;
            }
        }
        if (password is not null && !AuthRules.IsValidPassword(password))
        {
            reader.Add("value_error", "password", "Value error, Password must be 8-128 characters");
            password = null;
        }
        reader.ForbidExtraFields("username", "password");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new LoginRequest(username!, password!);
    }
}

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword)
{
    public static async Task<ChangePasswordRequest> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var current = reader.RequiredString("current_password");
        var next = reader.RequiredString("new_password");
        foreach (var (field, value) in new[] { ("current_password", current), ("new_password", next) })
        {
            if (value is not null && !AuthRules.IsValidPassword(value))
            {
                reader.Add("value_error", field, "Value error, Password must be 8-128 characters");
            }
        }
        reader.ForbidExtraFields("current_password", "new_password");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new ChangePasswordRequest(current!, next!);
    }
}

public sealed record AdminUserCreate(string Username, string DisplayName, string Password, string Role)
{
    public static async Task<AdminUserCreate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var username = reader.RequiredStringBefore("username", "Username");
        var displayName = reader.RequiredStringBefore("display_name", "Display name");
        var password = reader.RequiredString("password");
        var role = reader.RequiredEnum("role", UserRoles.All);
        if (username is not null)
        {
            if (!AuthRules.TryNormalizeUsername(username, out var normalizedUser, out var userError))
            {
                reader.Add("value_error", "username", $"Value error, {userError}");
                username = null;
            }
            else
            {
                username = normalizedUser;
            }
        }
        if (displayName is not null)
        {
            if (!AuthRules.TryNormalizeDisplayName(displayName, out var normalizedDisplay, out var displayError))
            {
                reader.Add("value_error", "display_name", $"Value error, {displayError}");
                displayName = null;
            }
            else
            {
                displayName = normalizedDisplay;
            }
        }
        if (password is not null && !AuthRules.IsValidPassword(password))
        {
            reader.Add("value_error", "password", "Value error, Password must be 8-128 characters");
            password = null;
        }
        reader.ForbidExtraFields("username", "display_name", "password", "role");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new AdminUserCreate(username!, displayName!, password!, role);
    }
}

/// <summary>Present fields tracked explicitly for PATCH semantics.</summary>
public sealed record AdminUserPatch(
    string? DisplayName,
    bool HasDisplayName,
    string? Role,
    bool HasRole,
    bool? IsActive,
    bool HasIsActive)
{
    public static async Task<AdminUserPatch> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var displayName = reader.OptionalStringBefore("display_name", "Display name");
        var hasDisplayName = body.Has("display_name");
        if (displayName is not null)
        {
            if (!AuthRules.TryNormalizeDisplayName(displayName, out var normalized, out var error))
            {
                reader.Add("value_error", "display_name", $"Value error, {error}");
                displayName = null;
            }
            else
            {
                displayName = normalized;
            }
        }
        var role = reader.OptionalEnum("role", UserRoles.All);
        var isActive = reader.OptionalBool("is_active");
        reader.ForbidExtraFields("display_name", "role", "is_active");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new AdminUserPatch(
            displayName,
            hasDisplayName,
            role,
            body.Has("role"),
            isActive,
            body.Has("is_active"));
    }
}

public sealed record AdminResetPassword(string NewPassword)
{
    public static async Task<AdminResetPassword> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var password = reader.RequiredString("new_password");
        if (password is not null && !AuthRules.IsValidPassword(password))
        {
            reader.Add("value_error", "new_password", "Value error, Password must be 8-128 characters");
            password = null;
        }
        reader.ForbidExtraFields("new_password");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new AdminResetPassword(password!);
    }
}

public sealed record ArtifactCreate(string Title, string Summary, string ContentMarkdown, List<int> FileIds)
{
    public const int TitleMaxLength = 200;
    public const int SummaryMaxLength = 500;
    public const int ContentMaxLength = 100_000;
    public const int FileIdsMaxLength = 10;

    public static async Task<ArtifactCreate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var title = ReadRequiredText(reader, "title", TitleMaxLength);
        var summary = ReadRequiredText(reader, "summary", SummaryMaxLength);
        var content = ReadRequiredText(reader, "content_markdown", ContentMaxLength);
        var fileIds = ReadFileIds(reader, "file_ids", allowNull: false, required: false, FileIdsMaxLength)
            ?? new List<int>();
        reader.ForbidExtraFields("title", "summary", "content_markdown", "file_ids");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new ArtifactCreate(title!, summary!, content!, fileIds);
    }

    internal static string? ReadRequiredText(FieldReader reader, string field, int maxLength)
    {
        var value = reader.RequiredString(field);
        if (value is null)
        {
            return null;
        }
        if (value.Length > maxLength)
        {
            reader.Add("string_too_long", field, $"String should have at most {maxLength} characters");
            return null;
        }
        var stripped = value.Trim();
        if (stripped.Length == 0)
        {
            reader.Add("value_error", field, "Value error, must not be blank");
            return null;
        }
        return stripped;
    }

    internal static List<int>? ReadFileIds(FieldReader reader, string field, bool allowNull, bool required, int maxLength)
    {
        if (!reader.HasField(field))
        {
            if (required)
            {
                reader.Add("missing", field, "Field required");
            }
            return required ? null : new List<int>();
        }
        var values = reader.OptionalIntList(field, allowNull, maxLength);
        if (values is null && !allowNull)
        {
            return new List<int>();
        }
        if (values is null or { Count: 0 })
        {
            return values;
        }
        if (values!.Any(fileId => fileId <= 0))
        {
            reader.Add("value_error", field, "Value error, file ids must be positive");
            return values;
        }
        if (values.Distinct().Count() != values.Count)
        {
            reader.Add("value_error", field, "Value error, file ids must be unique");
        }
        return values;
    }
}

public sealed record ArtifactUpdate(
    string? Title,
    bool HasTitle,
    string? Summary,
    bool HasSummary,
    string? ContentMarkdown,
    bool HasContentMarkdown,
    List<int>? FileIds,
    bool HasFileIds)
{
    public bool HasChanges => HasTitle || HasSummary || HasContentMarkdown || HasFileIds;

    public bool AnyExplicitNull =>
        (HasTitle && Title is null)
        || (HasSummary && Summary is null)
        || (HasContentMarkdown && ContentMarkdown is null)
        || (HasFileIds && FileIds is null);

    public static async Task<ArtifactUpdate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var title = ReadOptionalText(reader, "title", ArtifactCreate.TitleMaxLength);
        var summary = ReadOptionalText(reader, "summary", ArtifactCreate.SummaryMaxLength);
        var content = ReadOptionalText(reader, "content_markdown", ArtifactCreate.ContentMaxLength);
        List<int>? fileIds = null;
        if (reader.HasField("file_ids"))
        {
            fileIds = ArtifactCreate.ReadFileIds(reader, "file_ids", allowNull: true, required: true, ArtifactCreate.FileIdsMaxLength);
        }
        reader.ForbidExtraFields("title", "summary", "content_markdown", "file_ids");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new ArtifactUpdate(
            title,
            reader.HasField("title"),
            summary,
            reader.HasField("summary"),
            content,
            reader.HasField("content_markdown"),
            fileIds,
            reader.HasField("file_ids"));
    }

    private static string? ReadOptionalText(FieldReader reader, string field, int maxLength)
    {
        if (!reader.HasField(field))
        {
            return null;
        }
        var value = reader.OptionalString(field);
        if (value is null)
        {
            return null;
        }
        if (value.Length > maxLength)
        {
            reader.Add("string_too_long", field, $"String should have at most {maxLength} characters");
            return null;
        }
        var stripped = value.Trim();
        if (stripped.Length == 0)
        {
            reader.Add("value_error", field, "Value error, must not be blank");
            return null;
        }
        return stripped;
    }
}

public sealed record CommentCreate(string Content)
{
    public static async Task<CommentCreate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var value = reader.RequiredString("content");
        if (value is not null)
        {
            if (value.Length > 2_000)
            {
                reader.Add("string_too_long", "content", "String should have at most 2000 characters");
            }
            else if (value.Trim().Length == 0)
            {
                reader.Add("value_error", "content", "Value error, must not be blank");
            }
            else
            {
                value = value.Trim();
            }
        }
        reader.ForbidExtraFields("content");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new CommentCreate(value!);
    }
}

/// <summary>Shared required/optional text rules for the task schemas.</summary>
internal static class TextFields
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 100_000;

    public static string? ReadRequired(FieldReader reader, string field, int maxLength)
    {
        var value = reader.RequiredString(field);
        if (value is null)
        {
            return null;
        }
        if (value.Length > maxLength)
        {
            reader.Add("string_too_long", field, $"String should have at most {maxLength} characters");
            return null;
        }
        var stripped = value.Trim();
        if (stripped.Length == 0)
        {
            reader.Add("value_error", field, "Value error, must not be blank");
            return null;
        }
        return stripped;
    }

    public static string? ReadOptional(FieldReader reader, string field, int maxLength)
    {
        if (!reader.HasField(field))
        {
            return null;
        }
        var value = reader.OptionalString(field);
        if (value is null)
        {
            return null;
        }
        if (value.Length > maxLength)
        {
            reader.Add("string_too_long", field, $"String should have at most {maxLength} characters");
            return null;
        }
        var stripped = value.Trim();
        if (stripped.Length == 0)
        {
            reader.Add("value_error", field, "Value error, must not be blank");
            return null;
        }
        return stripped;
    }
}

public sealed record TaskCreate(string Title, string Description, DateTime? DeadlineAt)
{
    public static async Task<TaskCreate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var title = TextFields.ReadRequired(reader, "title", TextFields.TitleMaxLength);
        var description = TextFields.ReadRequired(reader, "description", TextFields.DescriptionMaxLength);
        var deadlineAt = reader.OptionalUtcDateTime("deadline_at");
        reader.ForbidExtraFields("title", "description", "deadline_at");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new TaskCreate(title!, description!, deadlineAt);
    }
}

/// <summary>Task patch: title/description may be explicitly null (rejected by the controller); deadline_at null clears it.</summary>
public sealed record TaskUpdate(
    string? Title,
    bool HasTitle,
    string? Description,
    bool HasDescription,
    DateTime? DeadlineAt,
    bool HasDeadlineAt)
{
    public bool AnyChanges => HasTitle || HasDescription || HasDeadlineAt;

    public static async Task<TaskUpdate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var title = TextFields.ReadOptional(reader, "title", TextFields.TitleMaxLength);
        var description = TextFields.ReadOptional(reader, "description", TextFields.DescriptionMaxLength);
        var deadlineAt = reader.OptionalUtcDateTime("deadline_at");
        reader.ForbidExtraFields("title", "description", "deadline_at");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new TaskUpdate(
            title,
            reader.HasField("title"),
            description,
            reader.HasField("description"),
            deadlineAt,
            reader.HasField("deadline_at"));
    }
}

public sealed record IssueCreate(string Title, string Description)
{
    public static async Task<IssueCreate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var title = TextFields.ReadRequired(reader, "title", TextFields.TitleMaxLength);
        var description = TextFields.ReadRequired(reader, "description", TextFields.DescriptionMaxLength);
        reader.ForbidExtraFields("title", "description");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new IssueCreate(title!, description!);
    }
}

public sealed record IssueUpdate(
    string? Title,
    bool HasTitle,
    string? Description,
    bool HasDescription)
{
    public bool AnyChanges => HasTitle || HasDescription;

    public static async Task<IssueUpdate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var title = TextFields.ReadOptional(reader, "title", TextFields.TitleMaxLength);
        var description = TextFields.ReadOptional(reader, "description", TextFields.DescriptionMaxLength);
        reader.ForbidExtraFields("title", "description");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new IssueUpdate(title, reader.HasField("title"), description, reader.HasField("description"));
    }
}

public sealed record CompetitionCreate(
    string Title,
    string Summary,
    string RulesMarkdown,
    DateTime StartAt,
    DateTime EndAt)
{
    public const int TitleMaxLength = 200;
    public const int SummaryMaxLength = 500;
    public const int RulesMaxLength = 100_000;

    public static async Task<CompetitionCreate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var title = TextFields.ReadRequired(reader, "title", TitleMaxLength);
        var summary = TextFields.ReadRequired(reader, "summary", SummaryMaxLength);
        var rules = TextFields.ReadRequired(reader, "rules_markdown", RulesMaxLength);
        var startAt = reader.RequiredUtcDateTime("start_at");
        var endAt = reader.RequiredUtcDateTime("end_at");
        if (startAt is not null && endAt is not null && startAt >= endAt)
        {
            reader.Add("value_error", new[] { "body" }, "Value error, start_at must be earlier than end_at");
        }
        reader.ForbidExtraFields("title", "summary", "rules_markdown", "start_at", "end_at");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new CompetitionCreate(title!, summary!, rules!, startAt!.Value, endAt!.Value);
    }
}

public sealed record CompetitionUpdate(
    string? Title,
    bool HasTitle,
    string? Summary,
    bool HasSummary,
    string? RulesMarkdown,
    bool HasRulesMarkdown,
    DateTime? StartAt,
    bool HasStartAt,
    DateTime? EndAt,
    bool HasEndAt)
{
    public bool AnyChanges => HasTitle || HasSummary || HasRulesMarkdown || HasStartAt || HasEndAt;

    public bool AnyExplicitNull =>
        (HasTitle && Title is null)
        || (HasSummary && Summary is null)
        || (HasRulesMarkdown && RulesMarkdown is null)
        || (HasStartAt && StartAt is null)
        || (HasEndAt && EndAt is null);

    public static async Task<CompetitionUpdate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var title = TextFields.ReadOptional(reader, "title", CompetitionCreate.TitleMaxLength);
        var summary = TextFields.ReadOptional(reader, "summary", CompetitionCreate.SummaryMaxLength);
        var rules = TextFields.ReadOptional(reader, "rules_markdown", CompetitionCreate.RulesMaxLength);
        var startAt = reader.OptionalUtcDateTime("start_at");
        var endAt = reader.OptionalUtcDateTime("end_at");
        if (reader.HasField("start_at") && reader.HasField("end_at")
            && startAt is not null && endAt is not null && startAt >= endAt)
        {
            reader.Add("value_error", new[] { "body" }, "Value error, start_at must be earlier than end_at");
        }
        reader.ForbidExtraFields("title", "summary", "rules_markdown", "start_at", "end_at");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new CompetitionUpdate(
            title,
            reader.HasField("title"),
            summary,
            reader.HasField("summary"),
            rules,
            reader.HasField("rules_markdown"),
            startAt,
            reader.HasField("start_at"),
            endAt,
            reader.HasField("end_at"));
    }
}
