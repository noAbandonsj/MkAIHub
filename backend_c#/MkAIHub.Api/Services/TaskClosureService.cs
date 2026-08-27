using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Api;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;

namespace MkAIHub.Api.Services;

/// <summary>
/// Task participation, submission, and decision services.
/// Mirrors app.services.task_closure; every closure state transition lives
/// here so the status machine stays in one place.
/// </summary>
public static class TaskClosureService
{
    public static readonly string Active = ParticipantStatuses.Active;
    public static readonly string Left = ParticipantStatuses.Left;

    public static readonly string Submitted = SubmissionStatuses.Submitted;
    public static readonly string RevisionRequired = SubmissionStatuses.RevisionRequired;
    public static readonly string Accepted = SubmissionStatuses.Accepted;
    public static readonly string Rejected = SubmissionStatuses.Rejected;

    public static readonly string[] PendingSubmissionStatuses = new[]
    {
        Submitted,
        RevisionRequired,
    };

    private static AppError StateConflict(string message)
        => new("TASK_STATE_CONFLICT", message, 409);

    private static bool IsTerminal(TaskItem task)
        => task.Status is TaskStatuses.Completed or TaskStatuses.Closed;

    public static bool IsAdmin(User user)
        => user.Role == UserRoles.SystemAdmin;

    public static IQueryable<TaskSubmission> WithDetails(this IQueryable<TaskSubmission> query)
        => query
            .Include(submission => submission.Task).ThenInclude(task => task.Competition)
            .Include(submission => submission.Participant).ThenInclude(participant => participant.User)
            .Include(submission => submission.Artifact)
            .Include(submission => submission.Decider)
            .Include(submission => submission.CompetitionReview).ThenInclude(review => review.Reviewer);

    public static async Task<TaskSubmission> GetSubmissionAsync(this AppDbContext db, int submissionId)
    {
        var submission = await db.TaskSubmissions
            .WithDetails()
            .FirstOrDefaultAsync(item => item.Id == submissionId);
        if (submission is null)
        {
            throw new AppError("SUBMISSION_NOT_FOUND", "Task submission not found", 404);
        }
        return submission;
    }

    public static async Task<TaskParticipant?> FindParticipantAsync(this AppDbContext db, int taskId, int userId)
        => await db.TaskParticipants
            .Include(participant => participant.User)
            .FirstOrDefaultAsync(participant => participant.TaskId == taskId && participant.UserId == userId);

    private static async Task<TaskParticipant> RequireActiveParticipantAsync(AppDbContext db, TaskItem task, User user)
    {
        var participant = await db.FindParticipantAsync(task.Id, user.Id);
        if (participant is null || participant.Status != Active)
        {
            throw new AppError("FORBIDDEN", "Only an active participant can submit to this task", 403);
        }
        return participant;
    }

    private static async Task<bool> HasActiveParticipantAsync(AppDbContext db, int taskId)
        => await db.TaskParticipants.AnyAsync(
            participant => participant.TaskId == taskId && participant.Status == Active);

    private static async Task<TaskSubmission?> CurrentSubmissionAsync(AppDbContext db, int participantId)
        => await db.TaskSubmissions.FirstOrDefaultAsync(
            submission => submission.ParticipantId == participantId && submission.IsCurrent);

    private static async Task<bool> HasPendingSubmissionAsync(AppDbContext db, int taskId)
        => await db.TaskSubmissions.AnyAsync(
            submission => submission.TaskId == taskId
                && submission.IsCurrent
                && PendingSubmissionStatuses.Contains(submission.Status));

    /// <summary>Only the author's own published artifacts can back a submission.</summary>
    public static async Task<Artifact> LoadSubmittableArtifactAsync(this AppDbContext db, User user, int artifactId)
    {
        var artifact = await db.Artifacts.FindAsync(artifactId);
        if (artifact is null)
        {
            throw new AppError("ARTIFACT_NOT_FOUND", "Artifact not found", 404);
        }
        if (artifact.AuthorId != user.Id || artifact.Status != ArtifactStatuses.Published)
        {
            throw new AppError(
                "ARTIFACT_NOT_SUBMITTABLE",
                "Only your published artifacts can be submitted",
                422);
        }
        return artifact;
    }

    /// <summary>
    /// Derive OPEN/IN_PROGRESS/REVIEWING from participants and pending rounds.
    /// Callers save their pending participant/submission changes first so the
    /// existence checks below see them (mirroring the Python db.flush()).
    /// </summary>
    private static async Task RecomputeTaskStatusAsync(AppDbContext db, TaskItem task)
    {
        if (IsTerminal(task) || task.CompetitionId is not null)
        {
            // Competition tasks only ever sit in OPEN/CLOSED; per-participant
            // progress lives in the submission rows, never in the task status.
            return;
        }
        string target;
        if (await HasPendingSubmissionAsync(db, task.Id))
        {
            target = TaskStatuses.Reviewing;
        }
        else if (await HasActiveParticipantAsync(db, task.Id))
        {
            target = TaskStatuses.InProgress;
        }
        else
        {
            target = TaskStatuses.Open;
        }
        if (task.Status != target)
        {
            task.Status = target;
            task.UpdatedAt = DateTime.UtcNow;
        }
    }

    public static async Task<TaskParticipant> JoinTaskAsync(this AppDbContext db, TaskItem task, User user)
    {
        if (IsTerminal(task))
        {
            throw StateConflict("A finished task cannot be joined");
        }
        if (task.CompetitionId is not null)
        {
            var competition = await db.Competitions.FindAsync(task.CompetitionId);
            if (competition is null || competition.Status != CompetitionLifecycles.Published)
            {
                throw new AppError(
                    "COMPETITION_STATE_CONFLICT",
                    "The competition does not accept participation",
                    409);
            }
            var registration = await db.FindRegistrationAsync(task.CompetitionId.Value, user.Id);
            if (registration is null || registration.Status != RegistrationStatuses.Registered)
            {
                throw new AppError(
                    "COMPETITION_REGISTRATION_REQUIRED",
                    "A valid registration is required to participate in this competition",
                    403);
            }
        }
        var participant = await db.FindParticipantAsync(task.Id, user.Id);
        var now = DateTime.UtcNow;
        if (participant is null)
        {
            participant = new TaskParticipant
            {
                TaskId = task.Id,
                UserId = user.Id,
                Status = Active,
                JoinedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                User = user,
            };
            db.TaskParticipants.Add(participant);
            await db.SaveChangesAsync();
        }
        else if (participant.Status == Active)
        {
            throw new AppError("TASK_ALREADY_PARTICIPATED", "You already participate in this task", 409);
        }
        else
        {
            participant.Status = Active;
            participant.JoinedAt = now;
            participant.LeftAt = null;
            participant.UpdatedAt = now;
            await db.SaveChangesAsync();
        }
        await RecomputeTaskStatusAsync(db, task);
        await db.SaveChangesAsync();
        return participant;
    }

    public static async Task<TaskParticipant> LeaveTaskAsync(this AppDbContext db, TaskItem task, User user)
    {
        if (task.CompetitionId is not null)
        {
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "Competition task participation is managed through competition registration",
                409);
        }
        var participant = await db.FindParticipantAsync(task.Id, user.Id);
        if (participant is null)
        {
            throw new AppError("PARTICIPANT_NOT_FOUND", "You do not participate in this task", 404);
        }
        if (IsTerminal(task))
        {
            throw StateConflict("A finished task cannot be left");
        }
        var hasSubmissions = await db.TaskSubmissions.AnyAsync(
            submission => submission.ParticipantId == participant.Id);
        if (hasSubmissions)
        {
            throw new AppError(
                "TASK_SUBMISSION_EXISTS",
                "A participant with submissions cannot leave the task",
                409);
        }
        participant.Status = Left;
        participant.LeftAt = DateTime.UtcNow;
        participant.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await RecomputeTaskStatusAsync(db, task);
        await db.SaveChangesAsync();
        return participant;
    }

    public static async Task<TaskSubmission> CreateSubmissionAsync(
        this AppDbContext db,
        TaskItem task,
        User user,
        int artifactId,
        string? note)
    {
        if (task.CompetitionId is not null)
        {
            // Competition tasks gate on registration and the deadline instead
            // of the standalone pending-round rule.
            return await db.CreateCompetitionSubmissionAsync(task, user, artifactId, note);
        }
        if (IsTerminal(task))
        {
            throw StateConflict("A finished task cannot receive submissions");
        }
        var participant = await RequireActiveParticipantAsync(db, task, user);
        var current = await CurrentSubmissionAsync(db, participant.Id);
        if (current is not null && (current.Status == Submitted || current.Status == Accepted))
        {
            throw new AppError(
                "SUBMISSION_ALREADY_PENDING",
                "The current submission is still pending or already accepted",
                409);
        }
        var artifact = await db.LoadSubmittableArtifactAsync(user, artifactId);
        if (current is not null)
        {
            current.IsCurrent = false;
        }
        var lastRound = await db.TaskSubmissions
            .Where(submission => submission.ParticipantId == participant.Id)
            .MaxAsync(submission => (int?)submission.RoundNo) ?? 0;
        var now = DateTime.UtcNow;
        var submission = new TaskSubmission
        {
            TaskId = task.Id,
            ParticipantId = participant.Id,
            ArtifactId = artifact.Id,
            RoundNo = lastRound + 1,
            Note = note,
            Status = Submitted,
            IsCurrent = true,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.TaskSubmissions.Add(submission);
        await db.SaveChangesAsync();
        await RecomputeTaskStatusAsync(db, task);
        await db.SaveChangesAsync();
        return await db.GetSubmissionAsync(submission.Id);
    }

    /// <summary>Apply request-revision / accept / reject to a current submission.</summary>
    public static async Task<TaskSubmission> DecideSubmissionAsync(
        this AppDbContext db,
        TaskSubmission submission,
        User user,
        string action,
        string? note)
    {
        var task = await db.Tasks.FindAsync(submission.TaskId);
        if (task is null)
        {
            throw new AppError("TASK_NOT_FOUND", "Task not found", 404);
        }
        if (task.CompetitionId is not null)
        {
            throw new AppError(
                "SUBMISSION_STATE_CONFLICT",
                "Competition submissions cannot be decided by accept-style actions",
                409);
        }
        if (task.CreatorId != user.Id && !IsAdmin(user))
        {
            throw new AppError("FORBIDDEN", "Only the task creator or an administrator can decide submissions", 403);
        }
        if (!submission.IsCurrent)
        {
            throw new AppError(
                "SUBMISSION_STATE_CONFLICT",
                "Only the current submission round can be decided",
                409);
        }
        var now = DateTime.UtcNow;
        if (action == "request_revision")
        {
            if (submission.Status != Submitted)
            {
                throw SubmissionStateConflict();
            }
            submission.Status = RevisionRequired;
            submission.RevisionRequestedAt = now;
        }
        else if (action == "accept")
        {
            if (submission.Status != Submitted && submission.Status != RevisionRequired)
            {
                throw SubmissionStateConflict();
            }
            submission.Status = Accepted;
            submission.DecidedAt = now;
        }
        else if (action == "reject")
        {
            if (submission.Status != Submitted && submission.Status != RevisionRequired)
            {
                throw SubmissionStateConflict();
            }
            submission.Status = Rejected;
            submission.DecidedAt = now;
        }
        else
        {
            throw new ArgumentException($"unknown decision action: {action}");
        }
        submission.DeciderId = user.Id;
        submission.DecisionNote = note;
        submission.UpdatedAt = now;
        await db.SaveChangesAsync();
        await RecomputeTaskStatusAsync(db, task);
        await db.SaveChangesAsync();
        return await db.GetSubmissionAsync(submission.Id);
    }

    private static AppError SubmissionStateConflict()
        => new(
            "SUBMISSION_STATE_CONFLICT",
            "The submission status does not allow this action",
            409);

    public static async Task<TaskItem> CompleteTaskAsync(this AppDbContext db, TaskItem task, User user)
    {
        if (task.CreatorId != user.Id)
        {
            throw new AppError("FORBIDDEN", "Only the task creator can complete this task", 403);
        }
        if (task.CompetitionId is not null)
        {
            throw StateConflict("A competition task is completed through result publication, not this action");
        }
        var acceptedCount = await db.TaskSubmissions.CountAsync(
            submission => submission.TaskId == task.Id && submission.Status == Accepted);
        if (acceptedCount == 0)
        {
            throw new AppError(
                "TASK_NO_ACCEPTED_RESULT",
                "The task cannot be completed without an accepted submission",
                409);
        }
        if (task.Status is not (TaskStatuses.InProgress or TaskStatuses.Reviewing))
        {
            throw StateConflict("Only an in-progress or reviewing task can be completed");
        }
        var now = DateTime.UtcNow;
        task.Status = TaskStatuses.Completed;
        task.CompletedAt = now;
        task.UpdatedAt = now;
        await db.SaveChangesAsync();
        return task;
    }

    public static async Task<TaskItem> CloseTaskAsync(this AppDbContext db, TaskItem task, User user)
    {
        if (task.CreatorId != user.Id && !IsAdmin(user))
        {
            throw new AppError("FORBIDDEN", "Task permission is required", 403);
        }
        if (IsTerminal(task))
        {
            throw StateConflict("A finished task cannot be closed");
        }
        var now = DateTime.UtcNow;
        task.Status = TaskStatuses.Closed;
        task.ClosedAt = now;
        task.UpdatedAt = now;
        await db.SaveChangesAsync();
        return task;
    }

    public static async Task<TaskItem> ReopenTaskAsync(this AppDbContext db, TaskItem task)
    {
        if (task.Status != TaskStatuses.Closed)
        {
            throw StateConflict("Only a closed task can be reopened");
        }
        if (task.CompetitionId is not null)
        {
            task.Status = TaskStatuses.Open;
        }
        else
        {
            task.Status = await HasActiveParticipantAsync(db, task.Id)
                ? TaskStatuses.InProgress
                : TaskStatuses.Open;
        }
        task.ClosedAt = null;
        task.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return task;
    }

    public static bool CanViewAllSubmissions(TaskItem task, User user)
        => task.CreatorId == user.Id || IsAdmin(user);

    public static TaskParticipantDto ToParticipantDto(TaskParticipant participant)
        => new(
            participant.Id,
            participant.TaskId,
            ArtifactService.ToSummary(participant.User!),
            participant.Status,
            participant.JoinedAt,
            participant.LeftAt);

    public static async Task<TaskSubmissionDto> ToSubmissionDtoAsync(
        this AppDbContext db,
        TaskSubmission submission,
        bool includeTask = false,
        User? viewer = null)
    {
        var task = submission.Task!;
        SubmissionTaskSummaryDto? taskSummary = null;
        if (includeTask)
        {
            var competition = task.Competition;
            taskSummary = new SubmissionTaskSummaryDto(
                task.Id,
                task.Title,
                task.Status,
                competition is not null
                    ? new TaskCompetitionSummaryDto(competition.Id, competition.Title, competition.Status)
                    : null);
        }
        SubmissionReviewSummaryDto? reviewSummary = null;
        int? competitionRank = null;
        string? competitionAward = null;
        var closureCompetition = task.CompetitionId is not null ? task.Competition : null;
        if (closureCompetition is not null && viewer is not null)
        {
            var resultsPublic = closureCompetition.Status
                is CompetitionLifecycles.ResultPublished or CompetitionLifecycles.Archived;
            var review = submission.CompetitionReview;
            if (review is not null && (resultsPublic || IsAdmin(viewer)))
            {
                reviewSummary = new SubmissionReviewSummaryDto(
                    FormatScore2(review.RawScore),
                    review.Comment,
                    review.ReviewedAt,
                    ArtifactService.ToSummary(review.Reviewer!));
            }
            if (resultsPublic)
            {
                var resultRow = await db.CompetitionResults
                    .Where(result => result.CompetitionId == closureCompetition.Id
                        && result.Registration!.UserId == submission.Participant!.UserId)
                    .FirstOrDefaultAsync();
                if (resultRow is not null)
                {
                    competitionRank = resultRow.Rank;
                    competitionAward = resultRow.Award;
                }
            }
        }
        var decider = submission.Decider;
        return new TaskSubmissionDto(
            submission.Id,
            submission.TaskId,
            submission.ParticipantId,
            ArtifactService.ToSummary(submission.Participant!.User!),
            new SubmissionArtifactSummaryDto(
                submission.Artifact!.Id,
                submission.Artifact.Title,
                submission.Artifact.Status),
            submission.RoundNo,
            submission.Note,
            submission.Status,
            submission.IsCurrent,
            submission.SubmittedAt,
            submission.RevisionRequestedAt,
            submission.DecidedAt,
            decider is not null ? ArtifactService.ToSummary(decider) : null,
            submission.DecisionNote,
            taskSummary,
            reviewSummary,
            competitionRank,
            competitionAward);
    }

    /// <summary>Format a score with exactly two decimals (Python f"{value:.2f}").</summary>
    public static string FormatScore2(decimal value)
        => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Format a total score with exactly four decimals (Python f"{value:.4f}").</summary>
    public static string FormatScore4(decimal value)
        => value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
}
