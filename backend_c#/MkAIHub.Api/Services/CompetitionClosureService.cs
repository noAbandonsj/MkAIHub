using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Api;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;

namespace MkAIHub.Api.Services;

/// <summary>One ranked participant row derived from current reviews.</summary>
public sealed class ComputedResult
{
    public CompetitionRegistration Registration { get; }

    public decimal TotalScore { get; }

    public int Rank { get; }

    public ComputedResult(CompetitionRegistration registration, decimal totalScore, int rank)
    {
        Registration = registration;
        TotalScore = totalScore;
        Rank = rank;
    }
}

/// <summary>One missing-review entry blocking result publication.</summary>
public sealed record MissingReview(string Username, int TaskId, string TaskTitle);

/// <summary>
/// Competition registration, task configuration, review, scoring, and results.
/// Mirrors app.services.competition_closure.
/// </summary>
public static class CompetitionClosureService
{
    private const int ScoreQuantumScale = 4;

    /// <summary>DRAFT competitions stay invisible to non-administrators (404).</summary>
    public static async Task<Competition> GetCompetitionForViewerAsync(
        this AppDbContext db, int competitionId, User user)
    {
        var competition = await db.GetCompetitionAsync(competitionId);
        if (competition.Status == CompetitionLifecycles.Draft
            && !TaskClosureService.IsAdmin(user))
        {
            throw new AppError("COMPETITION_NOT_FOUND", "Competition not found", 404);
        }
        return competition;
    }

    public static async Task<CompetitionRegistration?> FindRegistrationAsync(
        this AppDbContext db, int competitionId, int userId)
        => await db.CompetitionRegistrations
            .Include(registration => registration.User)
            .FirstOrDefaultAsync(registration =>
                registration.CompetitionId == competitionId && registration.UserId == userId);

    public static async Task<CompetitionRegistration> RegisterCompetitionAsync(
        this AppDbContext db, Competition competition, User user)
    {
        if (competition.Status != CompetitionLifecycles.Published)
        {
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "Only a published competition accepts registrations",
                409);
        }
        var now = DateTime.UtcNow;
        if (!(Database.AsUtc(competition.StartAt) <= now && now < Database.AsUtc(competition.EndAt)))
        {
            throw new AppError(
                "COMPETITION_REGISTRATION_CLOSED",
                "The registration window is closed",
                409);
        }
        var registration = await db.FindRegistrationAsync(competition.Id, user.Id);
        if (registration is null)
        {
            registration = new CompetitionRegistration
            {
                CompetitionId = competition.Id,
                UserId = user.Id,
                Status = RegistrationStatuses.Registered,
                RegisteredAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                User = user,
            };
            db.CompetitionRegistrations.Add(registration);
        }
        else if (registration.Status == RegistrationStatuses.Registered)
        {
            throw new AppError(
                "COMPETITION_ALREADY_REGISTERED",
                "You already registered for this competition",
                409);
        }
        else
        {
            registration.Status = RegistrationStatuses.Registered;
            registration.RegisteredAt = now;
            registration.CancelledAt = null;
            registration.UpdatedAt = now;
        }
        await db.SaveChangesAsync();
        return registration;
    }

    public static async Task<CompetitionRegistration> CancelRegistrationAsync(
        this AppDbContext db, Competition competition, User user)
    {
        var registration = await db.FindRegistrationAsync(competition.Id, user.Id);
        if (registration is null || registration.Status != RegistrationStatuses.Registered)
        {
            throw new AppError(
                "REGISTRATION_NOT_FOUND",
                "You are not registered for this competition",
                404);
        }
        if (DateTime.UtcNow >= Database.AsUtc(competition.EndAt))
        {
            throw new AppError(
                "COMPETITION_REGISTRATION_CLOSED",
                "The registration window is closed",
                409);
        }
        if (await db.UserHasCompetitionSubmissionsAsync(competition.Id, user.Id))
        {
            throw new AppError(
                "COMPETITION_SUBMISSION_EXISTS",
                "A registrant with submissions cannot cancel the registration",
                409);
        }
        registration.Status = RegistrationStatuses.Cancelled;
        registration.CancelledAt = DateTime.UtcNow;
        registration.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return registration;
    }

    public static async Task<bool> HasActiveRegistrationsAsync(this AppDbContext db, int competitionId)
        => await db.CompetitionRegistrations.AnyAsync(registration =>
            registration.CompetitionId == competitionId
            && registration.Status == RegistrationStatuses.Registered);

    public static async Task<bool> HasCompetitionSubmissionsAsync(this AppDbContext db, int competitionId)
        => await db.TaskSubmissions.AnyAsync(submission =>
            db.Tasks.Where(task => task.CompetitionId == competitionId)
                .Select(task => task.Id)
                .Contains(submission.TaskId));

    public static async Task<bool> UserHasCompetitionSubmissionsAsync(
        this AppDbContext db, int competitionId, int userId)
        => await db.TaskSubmissions.AnyAsync(submission =>
            db.TaskParticipants
                .Where(participant =>
                    participant.UserId == userId
                    && db.Tasks.Where(task => task.CompetitionId == competitionId)
                        .Select(task => task.Id)
                        .Contains(participant.TaskId))
                .Select(participant => participant.Id)
                .Contains(submission.ParticipantId));

    /// <summary>Any task, registration (even cancelled), or result blocks physical deletion.</summary>
    public static async Task<bool> CompetitionHasReferencesAsync(this AppDbContext db, int competitionId)
        => await db.Tasks.AnyAsync(task => task.CompetitionId == competitionId)
            || await db.CompetitionRegistrations.AnyAsync(registration =>
                registration.CompetitionId == competitionId)
            || await db.CompetitionResults.AnyAsync(result => result.CompetitionId == competitionId);

    /// <summary>Scoring configuration freezes once registrations or submissions exist.</summary>
    private static async Task<bool> ConfigLockedAsync(AppDbContext db, Competition competition)
        => await db.HasActiveRegistrationsAsync(competition.Id)
            || await db.HasCompetitionSubmissionsAsync(competition.Id);

    private static void ValidateTaskDeadline(Competition competition, DateTime? deadlineAt)
    {
        if (deadlineAt is not null
            && Database.AsUtc(deadlineAt.Value) > Database.AsUtc(competition.EndAt))
        {
            throw new AppError(
                "COMPETITION_TIME_CONFLICT",
                "A competition task deadline cannot be later than the competition end",
                422);
        }
    }

    public static async Task<TaskItem> CreateCompetitionTaskAsync(
        this AppDbContext db, Competition competition, User actor, CompetitionTaskCreate payload)
    {
        if (competition.Status is not (CompetitionLifecycles.Draft or CompetitionLifecycles.Published))
        {
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "Tasks can only be configured on draft or published competitions",
                409);
        }
        if (competition.Status == CompetitionLifecycles.Published && await ConfigLockedAsync(db, competition))
        {
            throw new AppError(
                "COMPETITION_CONFIG_LOCKED",
                "The competition configuration is locked by registrations or submissions",
                409);
        }
        ValidateTaskDeadline(competition, payload.DeadlineAt);
        var now = DateTime.UtcNow;
        var task = new TaskItem
        {
            Title = payload.Title,
            Description = payload.Description,
            DeadlineAt = payload.DeadlineAt,
            CreatorId = actor.Id,
            Status = TaskStatuses.Open,
            CompetitionId = competition.Id,
            CompetitionRequired = payload.Required,
            CompetitionSortOrder = payload.SortOrder,
            CompetitionMaxScore = payload.MaxScore,
            CompetitionWeight = payload.Weight,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();
        return await db.GetTaskAsync(task.Id);
    }

    public static async Task<TaskItem> GetCompetitionTaskAsync(
        this AppDbContext db, Competition competition, int taskId)
    {
        var task = await db.Tasks
            .WithCreator()
            .FirstOrDefaultAsync(item => item.Id == taskId && item.CompetitionId == competition.Id);
        if (task is null)
        {
            throw new AppError("TASK_NOT_FOUND", "Task not found", 404);
        }
        return task;
    }

    public static async Task<TaskItem> UpdateCompetitionTaskAsync(
        this AppDbContext db, Competition competition, TaskItem task, CompetitionTaskUpdate payload)
    {
        var fullEdit = competition.Status == CompetitionLifecycles.Draft
            || (competition.Status == CompetitionLifecycles.Published && !await ConfigLockedAsync(db, competition));
        if (!fullEdit && payload.TouchesConfig)
        {
            if (competition.Status == CompetitionLifecycles.Published)
            {
                throw new AppError(
                    "COMPETITION_CONFIG_LOCKED",
                    "Only the sort order can be changed after registrations or submissions",
                    409);
            }
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "Only the sort order can be changed on this competition",
                409);
        }
        if (payload.HasDeadlineAt)
        {
            ValidateTaskDeadline(competition, payload.DeadlineAt);
        }
        if (payload.HasTitle)
        {
            task.Title = payload.Title!;
        }
        if (payload.HasDescription)
        {
            task.Description = payload.Description!;
        }
        if (payload.HasDeadlineAt)
        {
            task.DeadlineAt = payload.DeadlineAt;
        }
        if (payload.HasRequired)
        {
            task.CompetitionRequired = payload.Required;
        }
        if (payload.HasSortOrder)
        {
            task.CompetitionSortOrder = payload.SortOrder;
        }
        if (payload.HasMaxScore)
        {
            task.CompetitionMaxScore = payload.MaxScore;
        }
        if (payload.HasWeight)
        {
            task.CompetitionWeight = payload.Weight;
        }
        if (payload.AnyChanges)
        {
            task.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return await db.GetCompetitionTaskAsync(competition, task.Id);
    }

    public static async Task DeleteCompetitionTaskAsync(this AppDbContext db, Competition competition, TaskItem task)
    {
        if (competition.Status != CompetitionLifecycles.Draft)
        {
            if (await ConfigLockedAsync(db, competition))
            {
                throw new AppError(
                    "COMPETITION_CONFIG_LOCKED",
                    "Tasks can only be deleted while the competition is a draft",
                    409);
            }
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "Tasks can only be deleted while the competition is a draft",
                409);
        }
        db.Tasks.Remove(task);
        await db.SaveChangesAsync();
    }

    public static async Task<Competition> PublishCompetitionAsync(this AppDbContext db, Competition competition)
    {
        if (competition.Status != CompetitionLifecycles.Draft)
        {
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "Only a draft competition can be published",
                409);
        }
        var taskCount = await db.Tasks.CountAsync(task => task.CompetitionId == competition.Id);
        if (taskCount == 0)
        {
            throw new AppError(
                "COMPETITION_NO_TASKS",
                "A competition needs at least one task before publishing",
                409);
        }
        competition.Status = CompetitionLifecycles.Published;
        competition.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return competition;
    }

    public static DateTime EffectiveDeadline(TaskItem task, Competition competition)
        => task.DeadlineAt is not null
            ? Database.AsUtc(task.DeadlineAt.Value)
            : Database.AsUtc(competition.EndAt);

    /// <summary>Submit a round to a competition task: registration and window gated.</summary>
    public static async Task<TaskSubmission> CreateCompetitionSubmissionAsync(
        this AppDbContext db, TaskItem task, User user, int artifactId, string? note)
    {
        var competition = await db.Competitions.FindAsync(task.CompetitionId);
        var participant = await db.FindParticipantAsync(task.Id, user.Id);
        if (participant is null || participant.Status != TaskClosureService.Active)
        {
            throw new AppError("FORBIDDEN", "Only an active participant can submit to this task", 403);
        }
        var registration = await db.FindRegistrationAsync(task.CompetitionId!.Value, user.Id);
        if (registration is null || registration.Status != RegistrationStatuses.Registered)
        {
            throw new AppError(
                "COMPETITION_REGISTRATION_REQUIRED",
                "A valid registration is required to submit to this competition",
                403);
        }
        if (task.Status != TaskStatuses.Open)
        {
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "The competition task is disabled",
                409);
        }
        if (competition is null || competition.Status != CompetitionLifecycles.Published)
        {
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "The competition does not accept submissions",
                409);
        }
        var now = DateTime.UtcNow;
        if (!(Database.AsUtc(competition.StartAt) <= now && now < EffectiveDeadline(task, competition)))
        {
            throw new AppError(
                "COMPETITION_SUBMISSION_CLOSED",
                "The competition task submission window is closed",
                409);
        }
        var artifact = await db.LoadSubmittableArtifactAsync(user, artifactId);
        var current = await db.TaskSubmissions.FirstOrDefaultAsync(submission =>
            submission.ParticipantId == participant.Id && submission.IsCurrent);
        if (current is not null)
        {
            current.IsCurrent = false;
        }
        var lastRound = await db.TaskSubmissions
            .Where(submission => submission.ParticipantId == participant.Id)
            .MaxAsync(submission => (int?)submission.RoundNo) ?? 0;
        var submission = new TaskSubmission
        {
            TaskId = task.Id,
            ParticipantId = participant.Id,
            ArtifactId = artifact.Id,
            RoundNo = lastRound + 1,
            Note = note,
            Status = SubmissionStatuses.Submitted,
            IsCurrent = true,
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.TaskSubmissions.Add(submission);
        await db.SaveChangesAsync();
        return await db.GetSubmissionAsync(submission.Id);
    }

    /// <summary>Upsert the single official review of a current competition submission.</summary>
    public static async Task<(CompetitionReview Review, bool Created)> ReviewCompetitionSubmissionAsync(
        this AppDbContext db, TaskSubmission submission, User reviewer, decimal rawScore, string? comment)
    {
        if (!TaskClosureService.IsAdmin(reviewer))
        {
            throw new AppError("FORBIDDEN", "Only administrators can review competition submissions", 403);
        }
        var task = submission.Task!;
        if (task.CompetitionId is null)
        {
            throw new AppError(
                "SUBMISSION_STATE_CONFLICT",
                "The submission does not belong to a competition task",
                409);
        }
        if (!submission.IsCurrent)
        {
            throw new AppError(
                "SUBMISSION_STATE_CONFLICT",
                "Only the current submission round can be reviewed",
                409);
        }
        var competition = task.Competition;
        if (competition is null || competition.Status != CompetitionLifecycles.Published)
        {
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "Reviews are only accepted while the competition is published",
                409);
        }
        var maxScore = task.CompetitionMaxScore ?? 0m;
        if (rawScore < 0 || rawScore > maxScore)
        {
            throw new AppError(
                "REVIEW_SCORE_INVALID",
                "The review score is outside the task score range",
                422,
                new Dictionary<string, object?> { ["max_score"] = TaskClosureService.FormatScore2(maxScore) });
        }
        var review = submission.CompetitionReview;
        var created = review is null;
        var now = DateTime.UtcNow;
        if (review is null)
        {
            review = new CompetitionReview
            {
                TaskSubmissionId = submission.Id,
                ReviewerId = reviewer.Id,
                RawScore = rawScore,
                Comment = comment,
                ReviewedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                Reviewer = reviewer,
            };
            db.CompetitionReviews.Add(review);
        }
        else
        {
            review.ReviewerId = reviewer.Id;
            review.RawScore = rawScore;
            review.Comment = comment;
            review.ReviewedAt = now;
            review.UpdatedAt = now;
        }
        await db.SaveChangesAsync();
        return (review, created);
    }

    /// <summary>Weighted totals with standard competition ranking; missing reviews reported.</summary>
    public static async Task<(List<ComputedResult> Results, List<MissingReview> Missing)> ComputeResultsAsync(
        this AppDbContext db, Competition competition)
    {
        var tasks = await db.Tasks
            .Where(task => task.CompetitionId == competition.Id)
            .OrderBy(task => task.CompetitionSortOrder)
            .ThenBy(task => task.Id)
            .ToListAsync();
        var registrations = await db.CompetitionRegistrations
            .Include(registration => registration.User)
            .Where(registration =>
                registration.CompetitionId == competition.Id
                && registration.Status == RegistrationStatuses.Registered)
            .OrderBy(registration => registration.Id)
            .ToListAsync();
        var taskIds = tasks.Select(task => task.Id).ToList();
        var currentByUser = new Dictionary<int, Dictionary<int, TaskSubmission>>();
        if (taskIds.Count > 0)
        {
            var rows = await db.TaskSubmissions
                .Where(submission => taskIds.Contains(submission.TaskId) && submission.IsCurrent)
                .Join(
                    db.TaskParticipants,
                    submission => submission.ParticipantId,
                    participant => participant.Id,
                    (submission, participant) => new { submission, participant.UserId })
                .ToListAsync();
            foreach (var row in rows)
            {
                if (!currentByUser.TryGetValue(row.UserId, out var byTask))
                {
                    byTask = new Dictionary<int, TaskSubmission>();
                    currentByUser[row.UserId] = byTask;
                }
                byTask[row.submission.TaskId] = row.submission;
            }
        }
        var submissionIds = currentByUser.Values
            .SelectMany(byTask => byTask.Values)
            .Select(submission => submission.Id)
            .ToList();
        var reviewsBySubmission = new Dictionary<int, CompetitionReview>();
        if (submissionIds.Count > 0)
        {
            var reviews = await db.CompetitionReviews
                .Where(review => submissionIds.Contains(review.TaskSubmissionId))
                .ToListAsync();
            foreach (var review in reviews)
            {
                reviewsBySubmission[review.TaskSubmissionId] = review;
            }
        }

        var missing = new List<MissingReview>();
        var qualified = new List<(CompetitionRegistration Registration, decimal Total)>();
        foreach (var registration in registrations)
        {
            currentByUser.TryGetValue(registration.UserId, out var byTask);
            byTask ??= new Dictionary<int, TaskSubmission>();
            var requiredMissing = tasks.Any(task => task.CompetitionRequired == true && !byTask.ContainsKey(task.Id));
            if (requiredMissing)
            {
                continue;
            }
            var total = 0m;
            foreach (var task in tasks)
            {
                if (!byTask.TryGetValue(task.Id, out var submission))
                {
                    continue; // Optional task without a submission scores zero.
                }
                if (!reviewsBySubmission.TryGetValue(submission.Id, out var review))
                {
                    missing.Add(new MissingReview(registration.User!.Username, task.Id, task.Title));
                    continue;
                }
                var taskScore = decimal.Round(
                    review.RawScore / task.CompetitionMaxScore!.Value * task.CompetitionWeight!.Value,
                    ScoreQuantumScale,
                    MidpointRounding.AwayFromZero);
                total += taskScore;
            }
            qualified.Add((registration, decimal.Round(total, ScoreQuantumScale, MidpointRounding.AwayFromZero)));
        }

        qualified.Sort((left, right) =>
        {
            var byScore = right.Total.CompareTo(left.Total);
            return byScore != 0 ? byScore : left.Registration.Id.CompareTo(right.Registration.Id);
        });
        var results = new List<ComputedResult>();
        decimal? previousTotal = null;
        var previousRank = 0;
        var index = 1;
        foreach (var (registration, total) in qualified)
        {
            var rank = previousTotal is null || total != previousTotal ? index : previousRank;
            results.Add(new ComputedResult(registration, total, rank));
            previousTotal = total;
            previousRank = rank;
            index += 1;
        }
        return (results, missing);
    }

    /// <summary>Freeze (or replace) the result snapshot; returns (republished, rows).</summary>
    public static async Task<(bool Republished, List<ComputedResult> Rows)> PublishResultsAsync(
        this AppDbContext db,
        Competition competition,
        User actor,
        IReadOnlyList<CompetitionAwardInput>? awards)
    {
        if (competition.Status
            is not (CompetitionLifecycles.Published or CompetitionLifecycles.ResultPublished))
        {
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "Only a published competition can publish or republish results",
                409);
        }
        var republished = competition.Status == CompetitionLifecycles.ResultPublished;
        if (!republished && DateTime.UtcNow < Database.AsUtc(competition.EndAt))
        {
            throw new AppError(
                "COMPETITION_NOT_ENDED",
                "Results can only be published after the competition ends",
                409);
        }
        var (results, missing) = await db.ComputeResultsAsync(competition);
        if (missing.Count > 0)
        {
            throw new AppError(
                "COMPETITION_REVIEWS_INCOMPLETE",
                "Current submissions without a review block the result publication",
                409,
                new Dictionary<string, object?>
                {
                    ["missing"] = missing
                        .Select(item => new Dictionary<string, object?>
                        {
                            ["username"] = item.Username,
                            ["task_id"] = item.TaskId,
                            ["task_title"] = item.TaskTitle,
                        })
                        .ToList(),
                });
        }
        var awardByRegistration = new Dictionary<int, string>();
        if (awards is { Count: > 0 })
        {
            var validIds = results.Select(row => row.Registration.Id).ToHashSet();
            foreach (var awardInput in awards)
            {
                if (!validIds.Contains(awardInput.RegistrationId)
                    || awardByRegistration.ContainsKey(awardInput.RegistrationId))
                {
                    throw new AppError(
                        "COMPETITION_AWARD_INVALID",
                        "Awards must map to ranked registrations exactly once",
                        422);
                }
                awardByRegistration[awardInput.RegistrationId] = awardInput.Award;
            }
        }
        var now = DateTime.UtcNow;
        var staleRows = await db.CompetitionResults
            .Where(result => result.CompetitionId == competition.Id)
            .ToListAsync();
        db.CompetitionResults.RemoveRange(staleRows);
        foreach (var row in results)
        {
            db.CompetitionResults.Add(new CompetitionResult
            {
                CompetitionId = competition.Id,
                RegistrationId = row.Registration.Id,
                TotalScore = row.TotalScore,
                Rank = row.Rank,
                Award = awardByRegistration.GetValueOrDefault(row.Registration.Id),
                PublishedBy = actor.Id,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        competition.Status = CompetitionLifecycles.ResultPublished;
        competition.UpdatedAt = now;
        await db.SaveChangesAsync();
        return (republished, results);
    }

    public static async Task<Competition> ArchiveCompetitionAsync(this AppDbContext db, Competition competition)
    {
        var canArchive = competition.Status == CompetitionLifecycles.ResultPublished
            || (competition.Status == CompetitionLifecycles.Published
                && !await db.HasActiveRegistrationsAsync(competition.Id));
        if (!canArchive)
        {
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "Only a result-published competition, or one without registrations, can be archived",
                409);
        }
        competition.Status = CompetitionLifecycles.Archived;
        competition.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return competition;
    }

    public static CompetitionRegistrationDto ToRegistrationDto(CompetitionRegistration registration)
        => new(
            registration.Id,
            registration.CompetitionId,
            ArtifactService.ToSummary(registration.User!),
            registration.Status,
            registration.RegisteredAt,
            registration.CancelledAt);

    public static async Task<List<CompetitionTaskDto>> CompetitionTasksReadAsync(
        this AppDbContext db, Competition competition, User viewer)
    {
        var tasks = await db.Tasks
            .Include(task => task.Creator)
            .Where(task => task.CompetitionId == competition.Id)
            .OrderBy(task => task.CompetitionSortOrder)
            .ThenBy(task => task.Id)
            .ToListAsync();
        var taskIds = tasks.Select(task => task.Id).ToList();
        var myCurrent = new Dictionary<int, TaskSubmission>();
        if (taskIds.Count > 0)
        {
            var rows = await db.TaskSubmissions
                .Where(submission => taskIds.Contains(submission.TaskId) && submission.IsCurrent)
                .Join(
                    db.TaskParticipants,
                    submission => submission.ParticipantId,
                    participant => participant.Id,
                    (submission, participant) => new { submission, participant })
                .Where(row => row.participant.UserId == viewer.Id)
                .ToListAsync();
            foreach (var row in rows)
            {
                myCurrent[row.participant.TaskId] = row.submission;
            }
        }
        var isAdmin = TaskClosureService.IsAdmin(viewer);
        var artifactTitles = new Dictionary<int, string>();
        var artifactIds = myCurrent.Values.Select(submission => submission.ArtifactId).Distinct().ToList();
        if (artifactIds.Count > 0)
        {
            var titles = await db.Artifacts
                .Where(artifact => artifactIds.Contains(artifact.Id))
                .Select(artifact => new { artifact.Id, artifact.Title })
                .ToListAsync();
            foreach (var title in titles)
            {
                artifactTitles[title.Id] = title.Title;
            }
        }
        var items = new List<CompetitionTaskDto>();
        foreach (var task in tasks)
        {
            myCurrent.TryGetValue(task.Id, out var submission);
            CompetitionTaskSubmissionSummaryDto? mySubmission = null;
            if (submission is not null)
            {
                mySubmission = new CompetitionTaskSubmissionSummaryDto(
                    submission.Id,
                    submission.ArtifactId,
                    artifactTitles.GetValueOrDefault(submission.ArtifactId, string.Empty),
                    submission.RoundNo,
                    submission.Status,
                    submission.IsCurrent,
                    submission.SubmittedAt);
            }
            int? currentSubmissionCount = null;
            int? reviewedCount = null;
            if (isAdmin)
            {
                currentSubmissionCount = await db.TaskSubmissions.CountAsync(item =>
                    item.TaskId == task.Id && item.IsCurrent);
                reviewedCount = await db.TaskSubmissions.CountAsync(item =>
                    item.TaskId == task.Id
                    && item.IsCurrent
                    && db.CompetitionReviews.Any(review => review.TaskSubmissionId == item.Id));
            }
            items.Add(new CompetitionTaskDto(
                task.Id,
                task.Title,
                task.Description,
                ArtifactService.ToSummary(task.Creator!),
                task.Status,
                task.CompetitionRequired == true,
                task.CompetitionSortOrder ?? 0,
                TaskClosureService.FormatScore2(task.CompetitionMaxScore ?? 0m),
                TaskClosureService.FormatScore2(task.CompetitionWeight ?? 0m),
                task.DeadlineAt,
                EffectiveDeadline(task, competition),
                mySubmission,
                currentSubmissionCount,
                reviewedCount,
                task.CreatedAt,
                task.UpdatedAt));
        }
        return items;
    }

    public static async Task<CompetitionResultsDto> ResultsReadAsync(this AppDbContext db, Competition competition)
    {
        var rows = await db.CompetitionResults
            .Include(result => result.Registration).ThenInclude(registration => registration.User)
            .Include(result => result.Publisher)
            .Where(result => result.CompetitionId == competition.Id)
            .OrderBy(result => result.Rank)
            .ThenByDescending(result => result.TotalScore)
            .ThenBy(result => result.Id)
            .ToListAsync();
        var first = rows.FirstOrDefault();
        return new CompetitionResultsDto(
            competition.Id,
            first is not null ? ArtifactService.ToSummary(first.Publisher!) : null,
            first?.PublishedAt,
            rows.Select(row => new CompetitionResultRowDto(
                row.RegistrationId,
                ArtifactService.ToSummary(row.Registration!.User!),
                TaskClosureService.FormatScore4(row.TotalScore),
                row.Rank,
                row.Award)).ToList());
    }
}
