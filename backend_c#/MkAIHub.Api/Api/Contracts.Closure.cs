using MkAIHub.Api.Core;

namespace MkAIHub.Api.Api;

/// <summary>DTOs and request payloads for the phase-2 closure loop.</summary>

public sealed record TaskParticipantDto(
    int Id,
    int TaskId,
    UserSummaryDto User,
    string Status,
    DateTime JoinedAt,
    DateTime? LeftAt);

public sealed record TaskParticipantListResponseDto(IReadOnlyList<TaskParticipantDto> Items);

public sealed record SubmissionArtifactSummaryDto(int Id, string Title, string Status);

public sealed record TaskCompetitionSummaryDto(int Id, string Title, string LifecycleStatus);

public sealed record SubmissionTaskSummaryDto(
    int Id,
    string Title,
    string Status,
    TaskCompetitionSummaryDto? Competition);

/// <summary>Review snapshot; the score is serialized as a 2-decimal string.</summary>
public sealed record SubmissionReviewSummaryDto(
    string RawScore,
    string? Comment,
    DateTime ReviewedAt,
    UserSummaryDto Reviewer);

public sealed record TaskSubmissionDto(
    int Id,
    int TaskId,
    int ParticipantId,
    UserSummaryDto Participant,
    SubmissionArtifactSummaryDto Artifact,
    int RoundNo,
    string? Note,
    string Status,
    bool IsCurrent,
    DateTime SubmittedAt,
    DateTime? RevisionRequestedAt,
    DateTime? DecidedAt,
    UserSummaryDto? Decider,
    string? DecisionNote,
    SubmissionTaskSummaryDto? Task,
    SubmissionReviewSummaryDto? CompetitionReview,
    int? CompetitionRank,
    string? CompetitionAward);

public sealed record TaskSubmissionListResponseDto(
    IReadOnlyList<TaskSubmissionDto> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record ArtifactTaskSourceListResponseDto(IReadOnlyList<TaskSubmissionDto> Items);

public sealed record CompetitionRegistrationSummaryDto(
    int Id,
    string Status,
    DateTime RegisteredAt,
    DateTime? CancelledAt);

public sealed record CompetitionRegistrationDto(
    int Id,
    int CompetitionId,
    UserSummaryDto User,
    string Status,
    DateTime RegisteredAt,
    DateTime? CancelledAt);

public sealed record CompetitionRegistrationListResponseDto(IReadOnlyList<CompetitionRegistrationDto> Items);

public sealed record CompetitionTaskSubmissionSummaryDto(
    int Id,
    int ArtifactId,
    string ArtifactTitle,
    int RoundNo,
    string Status,
    bool IsCurrent,
    DateTime SubmittedAt);

/// <summary>Competition task row; scores are serialized as 2-decimal strings.</summary>
public sealed record CompetitionTaskDto(
    int Id,
    string Title,
    string Description,
    UserSummaryDto Creator,
    string Status,
    bool Required,
    int SortOrder,
    string MaxScore,
    string Weight,
    DateTime? DeadlineAt,
    DateTime EffectiveDeadlineAt,
    CompetitionTaskSubmissionSummaryDto? MySubmission,
    int? CurrentSubmissionCount,
    int? ReviewedCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CompetitionTaskListResponseDto(IReadOnlyList<CompetitionTaskDto> Items);

public sealed record CompetitionResultRowDto(
    int RegistrationId,
    UserSummaryDto User,
    string TotalScore,
    int Rank,
    string? Award);

public sealed record CompetitionResultsDto(
    int CompetitionId,
    UserSummaryDto? PublishedBy,
    DateTime? PublishedAt,
    IReadOnlyList<CompetitionResultRowDto> Items);

public sealed record WorkbenchCountsDto(
    int ParticipatedTasks,
    int CompetitionTasks,
    int PendingTaskReviews,
    int PendingCompetitionReviews);

public sealed record ClosureStatisticsDto(
    int Participations,
    int Submissions,
    int AcceptedSubmissions,
    int CompetitionTaskCompletions,
    int PublishedResults);

public sealed record WorkbenchResponseDto(WorkbenchCountsDto Counts, ClosureStatisticsDto Statistics);

/// <summary>Shared note rules for submission payloads (strip; blank becomes null).</summary>
internal static class NoteFields
{
    public const int MaxLength = 2_000;

    public static string? ReadOptionalNote(FieldReader reader, string field)
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
        if (value.Length > MaxLength)
        {
            reader.Add("string_too_long", field, $"String should have at most {MaxLength} characters");
            return null;
        }
        var stripped = value.Trim();
        return stripped.Length == 0 ? null : stripped;
    }

    public static string? ReadRequiredNote(FieldReader reader, string field)
    {
        if (!reader.HasField(field))
        {
            reader.Add("missing", field, "Field required");
            return null;
        }
        var value = reader.OptionalString(field);
        if (value is null)
        {
            return null;
        }
        if (value.Length > MaxLength)
        {
            reader.Add("string_too_long", field, $"String should have at most {MaxLength} characters");
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

public sealed record TaskSubmissionCreate(int ArtifactId, string? Note)
{
    public static async Task<TaskSubmissionCreate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var artifactId = reader.RequiredInt("artifact_id");
        var note = NoteFields.ReadOptionalNote(reader, "note");
        reader.ForbidExtraFields("artifact_id", "note");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new TaskSubmissionCreate(artifactId, note);
    }
}

public sealed record SubmissionRevisionRequest(string Note)
{
    public static async Task<SubmissionRevisionRequest> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var note = NoteFields.ReadRequiredNote(reader, "note");
        reader.ForbidExtraFields("note");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new SubmissionRevisionRequest(note!);
    }
}

public sealed record SubmissionAcceptRequest(string? Note)
{
    /// <summary>The body is optional for the accept action (FastAPI "None" default).</summary>
    public static async Task<SubmissionAcceptRequest?> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.TryReadAsync(request);
        if (body is null)
        {
            return null;
        }
        var reader = new FieldReader(body);
        var note = NoteFields.ReadOptionalNote(reader, "note");
        reader.ForbidExtraFields("note");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new SubmissionAcceptRequest(note);
    }
}

public sealed record SubmissionRejectRequest(string Note)
{
    public static async Task<SubmissionRejectRequest> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var note = NoteFields.ReadRequiredNote(reader, "note");
        reader.ForbidExtraFields("note");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new SubmissionRejectRequest(note!);
    }
}

public sealed record CompetitionReviewRequest(decimal RawScore, string? Comment)
{
    public static async Task<CompetitionReviewRequest> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var rawScore = reader.RequiredDecimal("raw_score", maxDigits: 6, decimalPlaces: 2, greaterThanEqual: 0m);
        var comment = NoteFields.ReadOptionalNote(reader, "comment");
        reader.ForbidExtraFields("raw_score", "comment");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new CompetitionReviewRequest(rawScore!.Value, comment);
    }
}

public sealed record CompetitionAwardInput(int RegistrationId, string Award);

public sealed record PublishResultsRequest(IReadOnlyList<CompetitionAwardInput>? Awards)
{
    /// <summary>The body is optional for result publication.</summary>
    public static async Task<PublishResultsRequest?> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.TryReadAsync(request);
        if (body is null)
        {
            return null;
        }
        var reader = new FieldReader(body);
        List<CompetitionAwardInput>? awards = null;
        if (reader.HasField("awards"))
        {
            var element = body.Get("awards");
            if (element is not null && element.Value.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                if (element.Value.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    reader.Add("list_type", "awards", "Input should be a valid list");
                }
                else
                {
                    awards = new List<CompetitionAwardInput>();
                    var index = 0;
                    foreach (var item in element.Value.EnumerateArray())
                    {
                        if (item.ValueKind != System.Text.Json.JsonValueKind.Object)
                        {
                            reader.Add("model_type", new object[] { "body", "awards", index }, "Input should be a valid dictionary or object to extract fields from");
                        }
                        else
                        {
                            var award = ReadAward(item, reader, index);
                            if (award is not null)
                            {
                                awards.Add(award);
                            }
                        }
                        index += 1;
                    }
                }
            }
        }
        reader.ForbidExtraFields("awards");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new PublishResultsRequest(awards);
    }

    private static CompetitionAwardInput? ReadAward(
        System.Text.Json.JsonElement item, FieldReader reader, int index)
    {
        int? registrationId = null;
        string? award = null;
        foreach (var property in item.EnumerateObject())
        {
            switch (property.Name)
            {
                case "registration_id":
                    if (property.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                        && property.Value.TryGetInt32(out var parsed))
                    {
                        registrationId = parsed;
                    }
                    else
                    {
                        reader.Add("int_parsing", new object[] { "body", "awards", index, "registration_id" }, "Input should be a valid integer, unable to parse string as an integer");
                    }
                    break;
                case "award":
                    if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        award = property.Value.GetString()!.Trim();
                        if (award.Length == 0)
                        {
                            reader.Add("value_error", new object[] { "body", "awards", index, "award" }, "Value error, must not be blank");
                            award = null;
                        }
                        else if (award.Length > 200)
                        {
                            reader.Add("string_too_long", new object[] { "body", "awards", index, "award" }, "String should have at most 200 characters");
                            award = null;
                        }
                    }
                    else
                    {
                        reader.Add("string_type", new object[] { "body", "awards", index, "award" }, "Input should be a valid string");
                    }
                    break;
                default:
                    reader.Add("extra_forbidden", new object[] { "body", "awards", index, property.Name }, "Extra inputs are not permitted");
                    break;
            }
        }
        if (registrationId is null)
        {
            reader.Add("missing", new object[] { "body", "awards", index, "registration_id" }, "Field required");
        }
        if (award is null)
        {
            reader.Add("missing", new object[] { "body", "awards", index, "award" }, "Field required");
        }
        return registrationId is not null && award is not null
            ? new CompetitionAwardInput(registrationId.Value, award)
            : null;
    }
}

public sealed record CompetitionTaskCreate(
    string Title,
    string Description,
    DateTime? DeadlineAt,
    bool Required,
    int SortOrder,
    decimal MaxScore,
    decimal Weight)
{
    public static async Task<CompetitionTaskCreate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var title = TextFields.ReadRequired(reader, "title", TextFields.TitleMaxLength);
        var description = TextFields.ReadRequired(reader, "description", TextFields.DescriptionMaxLength);
        var deadlineAt = reader.OptionalUtcDateTime("deadline_at");
        var required = reader.RequiredBool("required");
        var sortOrder = ReadSortOrder(reader, "sort_order", required: true);
        var maxScore = reader.RequiredDecimal("max_score", maxDigits: 6, decimalPlaces: 2, greaterThan: 0m);
        var weight = reader.RequiredDecimal("weight", maxDigits: 5, decimalPlaces: 2, greaterThan: 0m);
        reader.ForbidExtraFields("title", "description", "deadline_at", "required", "sort_order", "max_score", "weight");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new CompetitionTaskCreate(
            title!,
            description!,
            deadlineAt,
            required != false,
            sortOrder!.Value,
            maxScore!.Value,
            weight!.Value);
    }

    internal static int? ReadSortOrder(FieldReader reader, string field, bool required)
    {
        if (!reader.HasField(field))
        {
            if (required)
            {
                reader.Add("missing", field, "Field required");
            }
            return null;
        }
        var element = reader.OptionalInt(field);
        if (element is null)
        {
            return null;
        }
        if (element < 0)
        {
            reader.Add("greater_than_equal", field, "Input should be greater than or equal to 0");
        }
        if (element > 9_999)
        {
            reader.Add("less_than_equal", field, "Input should be less than or equal to 9999");
        }
        return element;
    }
}

/// <summary>Competition task config patch; config fields may be locked by the service layer.</summary>
public sealed record CompetitionTaskUpdate(
    string? Title,
    bool HasTitle,
    string? Description,
    bool HasDescription,
    DateTime? DeadlineAt,
    bool HasDeadlineAt,
    bool? Required,
    bool HasRequired,
    int? SortOrder,
    bool HasSortOrder,
    decimal? MaxScore,
    bool HasMaxScore,
    decimal? Weight,
    bool HasWeight)
{
    public bool AnyChanges =>
        HasTitle || HasDescription || HasDeadlineAt || HasRequired || HasSortOrder || HasMaxScore || HasWeight;

    public bool TouchesConfig =>
        HasTitle || HasDescription || HasDeadlineAt || HasRequired || HasMaxScore || HasWeight;

    public IReadOnlyList<string> ChangedFields()
    {
        var fields = new List<string>();
        if (HasTitle) fields.Add("title");
        if (HasDescription) fields.Add("description");
        if (HasDeadlineAt) fields.Add("deadline_at");
        if (HasRequired) fields.Add("required");
        if (HasSortOrder) fields.Add("sort_order");
        if (HasMaxScore) fields.Add("max_score");
        if (HasWeight) fields.Add("weight");
        fields.Sort(StringComparer.Ordinal);
        return fields;
    }

    public static async Task<CompetitionTaskUpdate> ParseAsync(HttpRequest request)
    {
        var body = await JsonBody.ReadAsync(request);
        var reader = new FieldReader(body);
        var title = TextFields.ReadOptional(reader, "title", TextFields.TitleMaxLength);
        var description = TextFields.ReadOptional(reader, "description", TextFields.DescriptionMaxLength);
        var deadlineAt = reader.OptionalUtcDateTime("deadline_at");
        var required = reader.OptionalBool("required");
        var sortOrder = CompetitionTaskCreate.ReadSortOrder(reader, "sort_order", required: false);
        decimal? maxScore = null;
        decimal? weight = null;
        if (reader.HasField("max_score"))
        {
            maxScore = reader.OptionalDecimal("max_score", maxDigits: 6, decimalPlaces: 2, greaterThan: 0m);
        }
        if (reader.HasField("weight"))
        {
            weight = reader.OptionalDecimal("weight", maxDigits: 5, decimalPlaces: 2, greaterThan: 0m);
        }
        reader.ForbidExtraFields("title", "description", "deadline_at", "required", "sort_order", "max_score", "weight");
        if (reader.HasErrors)
        {
            throw reader.ToException();
        }
        return new CompetitionTaskUpdate(
            title,
            reader.HasField("title"),
            description,
            reader.HasField("description"),
            deadlineAt,
            reader.HasField("deadline_at"),
            required,
            reader.HasField("required"),
            sortOrder,
            reader.HasField("sort_order"),
            maxScore,
            reader.HasField("max_score"),
            weight,
            reader.HasField("weight"));
    }
}
