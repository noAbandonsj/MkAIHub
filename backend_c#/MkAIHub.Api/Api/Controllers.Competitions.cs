using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>Competition browsing and participation endpoints.</summary>
[Route("api/v1/competitions")]
public sealed class CompetitionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;

    public CompetitionsController(AppDbContext db, Authenticator auth)
    {
        _db = db;
        _auth = auth;
    }

    /// <summary>Searchable, paginated competition list.</summary>
    [HttpGet]
    public async Task<IActionResult> ListCompetitions(CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var page = QueryParams.ParseInt(Request, errors, "page", 1, minimum: 1);
        var pageSize = QueryParams.ParseInt(Request, errors, "page_size", 12, minimum: 1, maximum: 100);
        var q = QueryParams.ParseSearch(Request, errors, "q", 100);
        QueryParams.ThrowIfErrors(errors);

        IQueryable<Competition> query = _db.Competitions.WithCreator();
        if (auth.User.Role != UserRoles.SystemAdmin)
        {
            // DRAFT competitions are administrator-only until explicitly published.
            query = query.Where(competition => competition.Status != CompetitionLifecycles.Draft);
        }
        var search = q?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            var pattern = $"%{search}%";
            query = query.Where(competition =>
                EF.Functions.Like(competition.Title, pattern)
                || EF.Functions.Like(competition.Summary, pattern));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(competition => competition.StartAt)
            .ThenByDescending(competition => competition.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return this.SnakeJson(new CompetitionListResponseDto(
            items.Select(CompetitionService.ToListItem).ToList(),
            page,
            pageSize,
            total));
    }

    [HttpGet("{competitionId}")]
    public async Task<IActionResult> GetCompetition(string competitionId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionForViewerAsync(parsedId, auth.User);
        return this.SnakeJson(await _db.ToReadAsync(competition, auth.User));
    }

    /// <summary>Register for a competition as the current user.</summary>
    [HttpPost("{competitionId}/registrations")]
    public async Task<IActionResult> RegisterForCompetition(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionForViewerAsync(parsedId, auth.User);
        var registration = await _db.RegisterCompetitionAsync(competition, auth.User);
        return this.SnakeJson(CompetitionClosureService.ToRegistrationDto(registration), statusCode: 201);
    }

    /// <summary>Cancel my registration for a competition.</summary>
    [HttpDelete("{competitionId}/registrations/me")]
    public async Task<IActionResult> CancelMyRegistration(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionForViewerAsync(parsedId, auth.User);
        var registration = await _db.CancelRegistrationAsync(competition, auth.User);
        return this.SnakeJson(CompetitionClosureService.ToRegistrationDto(registration));
    }

    /// <summary>List competition tasks with my submission summaries.</summary>
    [HttpGet("{competitionId}/tasks")]
    public async Task<IActionResult> ListCompetitionTasks(string competitionId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionForViewerAsync(parsedId, auth.User);
        return this.SnakeJson(new CompetitionTaskListResponseDto(
            await _db.CompetitionTasksReadAsync(competition, auth.User)));
    }

    /// <summary>Read the frozen leaderboard snapshot.</summary>
    [HttpGet("{competitionId}/results")]
    public async Task<IActionResult> GetCompetitionResults(string competitionId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionForViewerAsync(parsedId, auth.User);
        if (competition.Status
            is not (CompetitionLifecycles.ResultPublished or CompetitionLifecycles.Archived))
        {
            throw new AppError(
                "COMPETITION_RESULTS_NOT_PUBLISHED",
                "Competition results have not been published",
                404);
        }
        return this.SnakeJson(await _db.ResultsReadAsync(competition));
    }
}

/// <summary>System-administrator competition maintenance endpoints.</summary>
[Route("api/v1/admin/competitions")]
public sealed class AdminCompetitionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;
    private readonly ILogger _logger;

    public AdminCompetitionsController(
        AppDbContext db,
        Authenticator auth,
        ILoggerFactory loggerFactory)
    {
        _db = db;
        _auth = auth;
        // The audit logger mirrors Python's "mkaihub" application logger.
        _logger = loggerFactory.CreateLogger("mkaihub");
    }

    /// <summary>Reject a stored window where the start is not before the end.</summary>
    private static void ValidateWindow(Competition competition)
    {
        if (Database.AsUtc(competition.StartAt) >= Database.AsUtc(competition.EndAt))
        {
            throw new AppError(
                "COMPETITION_TIME_CONFLICT",
                "start_at must be earlier than end_at",
                422);
        }
    }

    /// <summary>Create a competition announcement (starts as DRAFT).</summary>
    [HttpPost]
    public async Task<IActionResult> CreateCompetition(CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var payload = await CompetitionCreate.ParseAsync(Request);

        var now = DateTime.UtcNow;
        var competition = new Competition
        {
            Title = payload.Title,
            Summary = payload.Summary,
            RulesMarkdown = payload.RulesMarkdown,
            StartAt = payload.StartAt,
            EndAt = payload.EndAt,
            Status = CompetitionLifecycles.Draft,
            CreatedBy = auth.User.Id,
            Creator = auth.User,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Competitions.Add(competition);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogAdminAction(
            "competition.create",
            auth.User.Id,
            "competition",
            competition.Id,
            new Dictionary<string, object?> { ["title"] = competition.Title });
        var created = await _db.GetCompetitionAsync(competition.Id);
        return this.SnakeJson(await _db.ToReadAsync(created, auth.User), statusCode: 201);
    }

    /// <summary>Update competition fields; published competitions keep start_at locked.</summary>
    [HttpPatch("{competitionId}")]
    public async Task<IActionResult> UpdateCompetition(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        var payload = await CompetitionUpdate.ParseAsync(Request);
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedId);
        if (competition.Status
            is (CompetitionLifecycles.ResultPublished or CompetitionLifecycles.Archived))
        {
            throw new AppError(
                "COMPETITION_STATE_CONFLICT",
                "A result-published or archived competition cannot be edited",
                409);
        }
        if (payload.AnyExplicitNull)
        {
            throw new AppError("COMPETITION_FIELD_REQUIRED", "Competition fields cannot be null", 422);
        }
        var fields = new List<string>();
        if (payload.HasTitle)
        {
            fields.Add("title");
        }
        if (payload.HasSummary)
        {
            fields.Add("summary");
        }
        if (payload.HasRulesMarkdown)
        {
            fields.Add("rules_markdown");
        }
        if (payload.HasStartAt)
        {
            fields.Add("start_at");
            if (competition.Status == CompetitionLifecycles.Published)
            {
                // The edit form resubmits start_at unchanged; SQLite reads back
                // naive UTC, so normalize both sides before comparing.
                if (Database.AsUtc(payload.StartAt!.Value) != Database.AsUtc(competition.StartAt))
                {
                    throw new AppError(
                        "COMPETITION_FIELD_LOCKED",
                        "start_at cannot be changed after the competition is published",
                        422);
                }
                fields.Remove("start_at");
            }
        }
        if (payload.HasEndAt)
        {
            fields.Add("end_at");
        }
        if (payload.HasTitle)
        {
            competition.Title = payload.Title!;
        }
        if (payload.HasSummary)
        {
            competition.Summary = payload.Summary!;
        }
        if (payload.HasRulesMarkdown)
        {
            competition.RulesMarkdown = payload.RulesMarkdown!;
        }
        if (payload.HasStartAt && competition.Status != CompetitionLifecycles.Published)
        {
            competition.StartAt = payload.StartAt!.Value;
        }
        if (payload.HasEndAt)
        {
            competition.EndAt = payload.EndAt!.Value;
        }
        ValidateWindow(competition);
        if (fields.Count > 0)
        {
            competition.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        fields.Sort(StringComparer.Ordinal);
        _logger.LogAdminAction(
            "competition.update",
            auth.User.Id,
            "competition",
            competition.Id,
            new Dictionary<string, object?> { ["fields"] = fields });
        var updated = await _db.GetCompetitionAsync(competition.Id);
        return this.SnakeJson(await _db.ToReadAsync(updated, auth.User));
    }

    /// <summary>Delete a competition without tasks, registrations, or results.</summary>
    [HttpDelete("{competitionId}")]
    public async Task<IActionResult> DeleteCompetition(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedId);
        if (await _db.CompetitionHasReferencesAsync(competition.Id))
        {
            throw new AppError(
                "COMPETITION_HAS_REFERENCES",
                "A competition with tasks, registrations, or results cannot be deleted",
                409);
        }
        var competitionTitle = competition.Title;
        _db.Competitions.Remove(competition);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogAdminAction(
            "competition.delete",
            auth.User.Id,
            "competition",
            parsedId,
            new Dictionary<string, object?> { ["title"] = competitionTitle });
        return NoContent();
    }

    /// <summary>List the full registration roster for award assignment.</summary>
    [HttpGet("{competitionId}/registrations")]
    public async Task<IActionResult> ListCompetitionRegistrations(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        await _db.GetCompetitionAsync(parsedId);
        var registrations = await _db.CompetitionRegistrations
            .Include(registration => registration.User)
            .Where(registration => registration.CompetitionId == parsedId)
            .OrderBy(registration => registration.Id)
            .ToListAsync(cancellationToken);
        return this.SnakeJson(new CompetitionRegistrationListResponseDto(
            registrations.Select(CompetitionClosureService.ToRegistrationDto).ToList()));
    }

    /// <summary>Create a competition task.</summary>
    [HttpPost("{competitionId}/tasks")]
    public async Task<IActionResult> CreateCompetitionTask(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var payload = await CompetitionTaskCreate.ParseAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedId);
        var task = await _db.CreateCompetitionTaskAsync(competition, auth.User, payload);
        _logger.LogAdminAction(
            "competition.task_create",
            auth.User.Id,
            "task",
            task.Id,
            new Dictionary<string, object?>
            {
                ["competition_id"] = competition.Id,
                ["title"] = task.Title,
            });
        return this.SnakeJson(await _db.ToReadAsync(task, auth.User), statusCode: 201);
    }

    /// <summary>Update a competition task configuration.</summary>
    [HttpPatch("{competitionId}/tasks/{taskId}")]
    public async Task<IActionResult> UpdateCompetitionTask(
        string competitionId,
        string taskId,
        CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var payload = await CompetitionTaskUpdate.ParseAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedCompetitionId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        var parsedTaskId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedCompetitionId);
        var task = await _db.GetCompetitionTaskAsync(competition, parsedTaskId);
        task = await _db.UpdateCompetitionTaskAsync(competition, task, payload);
        _logger.LogAdminAction(
            "competition.task_update",
            auth.User.Id,
            "task",
            task.Id,
            new Dictionary<string, object?>
            {
                ["competition_id"] = competition.Id,
                ["fields"] = payload.ChangedFields(),
            });
        return this.SnakeJson(await _db.ToReadAsync(task, auth.User));
    }

    /// <summary>Delete a competition task (draft competitions only).</summary>
    [HttpDelete("{competitionId}/tasks/{taskId}")]
    public async Task<IActionResult> DeleteCompetitionTask(
        string competitionId,
        string taskId,
        CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedCompetitionId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        var parsedTaskId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedCompetitionId);
        var task = await _db.GetCompetitionTaskAsync(competition, parsedTaskId);
        await _db.DeleteCompetitionTaskAsync(competition, task);
        _logger.LogAdminAction(
            "competition.task_delete",
            auth.User.Id,
            "task",
            parsedTaskId,
            new Dictionary<string, object?>
            {
                ["competition_id"] = competition.Id,
                ["title"] = task.Title,
            });
        return NoContent();
    }

    /// <summary>Publish a draft competition with at least one task.</summary>
    [HttpPost("{competitionId}/publish")]
    public async Task<IActionResult> PublishCompetition(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedId);
        competition = await _db.PublishCompetitionAsync(competition);
        _logger.LogAdminAction(
            "competition.publish",
            auth.User.Id,
            "competition",
            competition.Id,
            new Dictionary<string, object?> { ["title"] = competition.Title });
        var published = await _db.GetCompetitionAsync(competition.Id);
        return this.SnakeJson(await _db.ToReadAsync(published, auth.User));
    }

    /// <summary>Publish (or republish) the frozen result snapshot.</summary>
    [HttpPost("{competitionId}/publish-results")]
    public async Task<IActionResult> PublishCompetitionResults(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var payload = await PublishResultsRequest.ParseAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedId);
        var (republished, rows) = await _db.PublishResultsAsync(
            competition, auth.User, payload?.Awards);
        _logger.LogAdminAction(
            republished ? "competition.republish_results" : "competition.publish_results",
            auth.User.Id,
            "competition",
            competition.Id,
            new Dictionary<string, object?> { ["result_count"] = rows.Count });
        var updated = await _db.GetCompetitionAsync(competition.Id);
        return this.SnakeJson(await _db.ToReadAsync(updated, auth.User));
    }

    /// <summary>Archive a result-published competition (or one without registrations).</summary>
    [HttpPost("{competitionId}/archive")]
    public async Task<IActionResult> ArchiveCompetition(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedId);
        competition = await _db.ArchiveCompetitionAsync(competition);
        _logger.LogAdminAction(
            "competition.archive",
            auth.User.Id,
            "competition",
            competition.Id,
            new Dictionary<string, object?> { ["title"] = competition.Title });
        var archived = await _db.GetCompetitionAsync(competition.Id);
        return this.SnakeJson(await _db.ToReadAsync(archived, auth.User));
    }
}
