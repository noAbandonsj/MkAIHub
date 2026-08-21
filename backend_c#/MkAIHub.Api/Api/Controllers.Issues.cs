using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>Issue CRUD and state-action endpoints.</summary>
[Route("api/v1/issues")]
public sealed class IssuesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;

    public IssuesController(AppDbContext db, Authenticator auth)
    {
        _db = db;
        _auth = auth;
    }

    private static AppError StateConflict(string message)
        => new("ISSUE_STATE_CONFLICT", message, 409);

    /// <summary>Filterable, paginated issue list.</summary>
    [HttpGet]
    public async Task<IActionResult> ListIssues(CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var page = QueryParams.ParseInt(Request, errors, "page", 1, minimum: 1);
        var pageSize = QueryParams.ParseInt(Request, errors, "page_size", 12, minimum: 1, maximum: 100);
        var q = QueryParams.ParseSearch(Request, errors, "q", 100);
        var mine = QueryParams.ParseBool(Request, errors, "mine");
        var statusFilter = QueryParams.ParseEnum(Request, errors, "status", IssueStatuses.All);
        QueryParams.ThrowIfErrors(errors);

        IQueryable<Issue> query = _db.Issues.WithAuthor();
        if (mine)
        {
            query = query.Where(issue => issue.AuthorId == auth.User.Id);
        }
        if (statusFilter is not null)
        {
            query = query.Where(issue => issue.Status == statusFilter);
        }
        var search = q?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            var pattern = $"%{search}%";
            query = query.Where(issue =>
                EF.Functions.Like(issue.Title, pattern)
                || EF.Functions.Like(issue.Description, pattern));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(issue => issue.UpdatedAt)
            .ThenByDescending(issue => issue.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return this.SnakeJson(new IssueListResponseDto(
            items.Select(IssueService.ToListItem).ToList(),
            page,
            pageSize,
            total));
    }

    /// <summary>Create an issue authored by the current user.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateIssue(CancellationToken cancellationToken)
    {
        var payload = await IssueCreate.ParseAsync(Request);
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));

        var now = DateTime.UtcNow;
        var issue = new Issue
        {
            Title = payload.Title,
            Description = payload.Description,
            AuthorId = auth.User.Id,
            Author = auth.User,
            Status = IssueStatuses.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Issues.Add(issue);
        await _db.SaveChangesAsync(cancellationToken);
        var created = await _db.GetIssueAsync(issue.Id);
        return this.SnakeJson(IssueService.ToRead(created), statusCode: 201);
    }

    [HttpGet("{issueId}")]
    public async Task<IActionResult> GetIssue(string issueId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "issue_id", "issueId");
        QueryParams.ThrowIfErrors(errors);

        var issue = await _db.GetIssueAsync(parsedId);
        return this.SnakeJson(IssueService.ToRead(issue));
    }

    /// <summary>Update an open issue (author only).</summary>
    [HttpPatch("{issueId}")]
    public async Task<IActionResult> UpdateIssue(string issueId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "issue_id", "issueId");
        var payload = await IssueUpdate.ParseAsync(Request);
        QueryParams.ThrowIfErrors(errors);

        var issue = await _db.GetIssueAsync(parsedId);
        IssueService.RequireIssueAuthor(issue, auth.User);
        if (issue.Status != IssueStatuses.Open)
        {
            throw StateConflict("Reopen the issue before editing it");
        }

        if ((payload.HasTitle && payload.Title is null)
            || (payload.HasDescription && payload.Description is null))
        {
            throw new AppError("ISSUE_FIELD_REQUIRED", "Issue fields cannot be null", 422);
        }
        if (payload.HasTitle)
        {
            issue.Title = payload.Title!;
        }
        if (payload.HasDescription)
        {
            issue.Description = payload.Description!;
        }
        if (payload.AnyChanges)
        {
            issue.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        var updated = await _db.GetIssueAsync(issue.Id);
        return this.SnakeJson(IssueService.ToRead(updated));
    }

    /// <summary>Close an open issue (author or administrator).</summary>
    [HttpPost("{issueId}/close")]
    public async Task<IActionResult> CloseIssue(string issueId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "issue_id", "issueId");
        QueryParams.ThrowIfErrors(errors);

        var issue = await _db.GetIssueAsync(parsedId);
        IssueService.RequireIssueAuthorOrAdmin(issue, auth.User);
        if (issue.Status != IssueStatuses.Open)
        {
            throw StateConflict("Only an open issue can be closed");
        }
        var now = DateTime.UtcNow;
        issue.Status = IssueStatuses.Closed;
        issue.ClosedAt = now;
        issue.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        var closed = await _db.GetIssueAsync(issue.Id);
        return this.SnakeJson(IssueService.ToRead(closed));
    }

    /// <summary>Reopen a closed issue (author or administrator).</summary>
    [HttpPost("{issueId}/reopen")]
    public async Task<IActionResult> ReopenIssue(string issueId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "issue_id", "issueId");
        QueryParams.ThrowIfErrors(errors);

        var issue = await _db.GetIssueAsync(parsedId);
        IssueService.RequireIssueAuthorOrAdmin(issue, auth.User);
        if (issue.Status != IssueStatuses.Closed)
        {
            throw StateConflict("Only a closed issue can be reopened");
        }
        issue.Status = IssueStatuses.Open;
        issue.ClosedAt = null;
        issue.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        var reopened = await _db.GetIssueAsync(issue.Id);
        return this.SnakeJson(IssueService.ToRead(reopened));
    }
}

/// <summary>Issue comment endpoints.</summary>
[Route("api/v1/issues/{issueId}/comments")]
public sealed class IssueCommentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;

    public IssueCommentsController(AppDbContext db, Authenticator auth)
    {
        _db = db;
        _auth = auth;
    }

    /// <summary>Issue comments, oldest first; hidden ones stay visible to administrators.</summary>
    [HttpGet]
    public async Task<IActionResult> ListComments(string issueId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "issue_id", "issueId");
        var page = QueryParams.ParseInt(Request, errors, "page", 1, minimum: 1);
        var pageSize = QueryParams.ParseInt(Request, errors, "page_size", 50, minimum: 1, maximum: 100);
        QueryParams.ThrowIfErrors(errors);

        await _db.GetIssueAsync(parsedId);
        IQueryable<Comment> query = _db.Comments.Include(comment => comment.Author);
        query = query.Where(comment => comment.IssueId == parsedId);
        if (auth.User.Role != UserRoles.SystemAdmin)
        {
            query = query.Where(comment => comment.Status == CommentStatuses.Visible);
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(comment => comment.CreatedAt)
            .ThenBy(comment => comment.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return this.SnakeJson(new CommentListResponseDto(
            items.Select(ArtifactService.ToCommentRead).ToList(),
            page,
            pageSize,
            total));
    }

    /// <summary>Comment on an open issue.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateComment(string issueId, CancellationToken cancellationToken)
    {
        var payload = await CommentCreate.ParseAsync(Request);
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "issue_id", "issueId");
        QueryParams.ThrowIfErrors(errors);

        var issue = await _db.GetIssueAsync(parsedId);
        if (issue.Status != IssueStatuses.Open)
        {
            throw new AppError(
                "ISSUE_STATE_CONFLICT",
                "Only an open issue can receive comments",
                409);
        }
        var now = DateTime.UtcNow;
        var comment = new Comment
        {
            IssueId = issue.Id,
            AuthorId = auth.User.Id,
            Author = auth.User,
            Content = payload.Content,
            Status = CommentStatuses.Visible,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Comments.Add(comment);
        await _db.SaveChangesAsync(cancellationToken);
        return this.SnakeJson(ArtifactService.ToCommentRead(comment), statusCode: 201);
    }
}
