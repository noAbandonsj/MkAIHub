using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>Competition browsing endpoints.</summary>
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
        await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var page = QueryParams.ParseInt(Request, errors, "page", 1, minimum: 1);
        var pageSize = QueryParams.ParseInt(Request, errors, "page_size", 12, minimum: 1, maximum: 100);
        var q = QueryParams.ParseSearch(Request, errors, "q", 100);
        QueryParams.ThrowIfErrors(errors);

        IQueryable<Competition> query = _db.Competitions.WithCreator();
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
        await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedId);
        return this.SnakeJson(CompetitionService.ToRead(competition));
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
        if (competition.StartAt >= competition.EndAt)
        {
            throw new AppError(
                "COMPETITION_TIME_CONFLICT",
                "start_at must be earlier than end_at",
                422);
        }
    }

    /// <summary>Create a competition announcement.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateCompetition(CancellationToken cancellationToken)
    {
        var payload = await CompetitionCreate.ParseAsync(Request);
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));

        var now = DateTime.UtcNow;
        var competition = new Competition
        {
            Title = payload.Title,
            Summary = payload.Summary,
            RulesMarkdown = payload.RulesMarkdown,
            StartAt = payload.StartAt,
            EndAt = payload.EndAt,
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
        return this.SnakeJson(CompetitionService.ToRead(created), statusCode: 201);
    }

    /// <summary>Update competition fields; the merged window must stay valid.</summary>
    [HttpPatch("{competitionId}")]
    public async Task<IActionResult> UpdateCompetition(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        var payload = await CompetitionUpdate.ParseAsync(Request);
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedId);
        if (payload.AnyExplicitNull)
        {
            throw new AppError("COMPETITION_FIELD_REQUIRED", "Competition fields cannot be null", 422);
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
        if (payload.HasStartAt)
        {
            competition.StartAt = payload.StartAt!.Value;
        }
        if (payload.HasEndAt)
        {
            competition.EndAt = payload.EndAt!.Value;
        }
        ValidateWindow(competition);
        if (payload.AnyChanges)
        {
            competition.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
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
        }
        if (payload.HasEndAt)
        {
            fields.Add("end_at");
        }
        fields.Sort(StringComparer.Ordinal);
        _logger.LogAdminAction(
            "competition.update",
            auth.User.Id,
            "competition",
            competition.Id,
            new Dictionary<string, object?> { ["fields"] = fields });
        var updated = await _db.GetCompetitionAsync(competition.Id);
        return this.SnakeJson(CompetitionService.ToRead(updated));
    }

    /// <summary>Delete a competition announcement.</summary>
    [HttpDelete("{competitionId}")]
    public async Task<IActionResult> DeleteCompetition(string competitionId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "competition_id", "competitionId");
        QueryParams.ThrowIfErrors(errors);

        var competition = await _db.GetCompetitionAsync(parsedId);
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
}
