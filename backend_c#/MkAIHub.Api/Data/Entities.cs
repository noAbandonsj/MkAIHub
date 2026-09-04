namespace MkAIHub.Api.Data;

/// <summary>An employee or system administrator account.</summary>
public sealed class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<UserSession> Sessions { get; set; } = new();
}

/// <summary>A revocable server-side session identified by a hashed cookie token.</summary>
public sealed class UserSession
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public User? User { get; set; }
}

/// <summary>A file stored below the configured upload directory.</summary>
public sealed class StoredFile
{
    public int Id { get; set; }

    public string OriginalName { get; set; } = string.Empty;

    public string StoredName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string Extension { get; set; } = string.Empty;

    public string MimeType { get; set; } = string.Empty;

    public int SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public int UploaderId { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public User? Uploader { get; set; }

    public List<ArtifactFile> ArtifactLinks { get; set; } = new();
}

/// <summary>A lightweight internal AI sharing item.</summary>
public sealed class Artifact
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string ContentMarkdown { get; set; } = string.Empty;

    public int AuthorId { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime? PublishedAt { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User? Author { get; set; }

    public List<ArtifactFile> FileLinks { get; set; } = new();

    public List<Comment> Comments { get; set; } = new();

    public List<TaskSubmission> TaskSubmissions { get; set; } = new();
}

/// <summary>Ordered attachment link between an artifact and a stored file.</summary>
public sealed class ArtifactFile
{
    public int Id { get; set; }

    public int ArtifactId { get; set; }

    public int FileId { get; set; }

    public int SortOrder { get; set; }

    public Artifact? Artifact { get; set; }

    public StoredFile? File { get; set; }
}

/// <summary>A comment attached to exactly one artifact or issue.</summary>
public sealed class Comment
{
    public int Id { get; set; }

    public int? ArtifactId { get; set; }

    public int? IssueId { get; set; }

    public int AuthorId { get; set; }

    public string Content { get; set; } = string.Empty;

    public string Status { get; set; } = "VISIBLE";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Artifact? Artifact { get; set; }

    public Issue? Issue { get; set; }

    public User? Author { get; set; }
}

/// <summary>
/// A lightweight task owned by its creator. Named TaskItem to avoid clashing
/// with System.Threading.Tasks.Task; the table stays "tasks".
/// </summary>
public sealed class TaskItem
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int CreatorId { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>NULL keeps standalone-task semantics; a set value marks a competition task.</summary>
    public int? CompetitionId { get; set; }

    public bool? CompetitionRequired { get; set; }

    public int? CompetitionSortOrder { get; set; }

    public decimal? CompetitionMaxScore { get; set; }

    public decimal? CompetitionWeight { get; set; }

    public DateTime? DeadlineAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User? Creator { get; set; }

    public Competition? Competition { get; set; }
}

/// <summary>One user's current participation record on one task.</summary>
public sealed class TaskParticipant
{
    public int Id { get; set; }

    public int TaskId { get; set; }

    public int UserId { get; set; }

    public string Status { get; set; } = "ACTIVE";

    public DateTime JoinedAt { get; set; }

    public DateTime? LeftAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public TaskItem? Task { get; set; }

    public User? User { get; set; }
}

/// <summary>One submitted artifact round of one participant on one task.</summary>
public sealed class TaskSubmission
{
    public int Id { get; set; }

    public int TaskId { get; set; }

    public int ParticipantId { get; set; }

    public int ArtifactId { get; set; }

    public int RoundNo { get; set; }

    public string? Note { get; set; }

    public string Status { get; set; } = "SUBMITTED";

    public bool IsCurrent { get; set; } = true;

    public DateTime SubmittedAt { get; set; }

    public DateTime? RevisionRequestedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public int? DeciderId { get; set; }

    public string? DecisionNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public TaskItem? Task { get; set; }

    public TaskParticipant? Participant { get; set; }

    public Artifact? Artifact { get; set; }

    public User? Decider { get; set; }

    public CompetitionReview? CompetitionReview { get; set; }
}

/// <summary>An internal problem report or discussion thread.</summary>
public sealed class Issue
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int AuthorId { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime? ClosedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User? Author { get; set; }

    public List<Comment> Comments { get; set; } = new();
}

/// <summary>A time-boxed internal competition announcement.</summary>
public sealed class Competition
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string RulesMarkdown { get; set; } = string.Empty;

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    /// <summary>Operational lifecycle (DRAFT/PUBLISHED/RESULT_PUBLISHED/ARCHIVED); the display state stays derived from time.</summary>
    public string Status { get; set; } = "DRAFT";

    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User? Creator { get; set; }
}

/// <summary>One user's current registration record on one competition.</summary>
public sealed class CompetitionRegistration
{
    public int Id { get; set; }

    public int CompetitionId { get; set; }

    public int UserId { get; set; }

    public string Status { get; set; } = "REGISTERED";

    public DateTime RegisteredAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Competition? Competition { get; set; }

    public User? User { get; set; }
}

/// <summary>The single official review row of one task submission.</summary>
public sealed class CompetitionReview
{
    public int Id { get; set; }

    public int TaskSubmissionId { get; set; }

    public int ReviewerId { get; set; }

    public decimal RawScore { get; set; }

    public string? Comment { get; set; }

    public DateTime ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public TaskSubmission? Submission { get; set; }

    public User? Reviewer { get; set; }
}

/// <summary>A frozen leaderboard snapshot row published once per competition.</summary>
public sealed class CompetitionResult
{
    public int Id { get; set; }

    public int CompetitionId { get; set; }

    public int RegistrationId { get; set; }

    public decimal TotalScore { get; set; }

    public int Rank { get; set; }

    public string? Award { get; set; }

    public int PublishedBy { get; set; }

    public DateTime PublishedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Competition? Competition { get; set; }

    public CompetitionRegistration? Registration { get; set; }

    public User? Publisher { get; set; }
}

public static class UserRoles
{
    public const string Employee = "EMPLOYEE";
    public const string SystemAdmin = "SYSTEM_ADMIN";

    public static readonly IReadOnlyList<string> All = new[] { Employee, SystemAdmin };
}

public static class ArtifactStatuses
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";
    public const string Archived = "ARCHIVED";

    public static readonly IReadOnlyList<string> All = new[] { Draft, Published, Archived };
}

public static class CommentStatuses
{
    public const string Visible = "VISIBLE";
    public const string Hidden = "HIDDEN";
}

public static class TaskStatuses
{
    public const string Open = "OPEN";
    public const string InProgress = "IN_PROGRESS";
    public const string Reviewing = "REVIEWING";
    public const string Completed = "COMPLETED";
    public const string Closed = "CLOSED";

    public static readonly IReadOnlyList<string> All = new[] { Open, InProgress, Reviewing, Completed, Closed };
}

public static class ParticipantStatuses
{
    public const string Active = "ACTIVE";
    public const string Left = "LEFT";
}

public static class SubmissionStatuses
{
    public const string Submitted = "SUBMITTED";
    public const string RevisionRequired = "REVISION_REQUIRED";
    public const string Accepted = "ACCEPTED";
    public const string Rejected = "REJECTED";
}

public static class IssueStatuses
{
    public const string Open = "OPEN";
    public const string Closed = "CLOSED";

    public static readonly IReadOnlyList<string> All = new[] { Open, Closed };
}

public static class CompetitionStatuses
{
    public const string Upcoming = "UPCOMING";
    public const string Ongoing = "ONGOING";
    public const string Ended = "ENDED";
}

/// <summary>Operational competition lifecycle, distinct from the derived time status.</summary>
public static class CompetitionLifecycles
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";
    public const string ResultPublished = "RESULT_PUBLISHED";
    public const string Archived = "ARCHIVED";
}

public static class RegistrationStatuses
{
    public const string Registered = "REGISTERED";
    public const string Cancelled = "CANCELLED";
}

public static class ArtifactSources
{
    public const string TaskResult = "TASK_RESULT";
    public const string CompetitionEntry = "COMPETITION_ENTRY";

    public static readonly IReadOnlyList<string> All = new[] { TaskResult, CompetitionEntry };
}
