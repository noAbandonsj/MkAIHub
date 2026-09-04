using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>Artifact CRUD and state-action endpoints.</summary>
[Route("api/v1/artifacts")]
public sealed class ArtifactsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;

    public ArtifactsController(AppDbContext db, Authenticator auth)
    {
        _db = db;
        _auth = auth;
    }

    private static AppError StateConflict(string message)
        => new("ARTIFACT_STATE_CONFLICT", message, 409);

    /// <summary>Filterable, paginated artifact list.</summary>
    [HttpGet]
    public async Task<IActionResult> ListArtifacts(CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var page = QueryParams.ParseInt(Request, errors, "page", 1, minimum: 1);
        var pageSize = QueryParams.ParseInt(Request, errors, "page_size", 12, minimum: 1, maximum: 100);
        var q = QueryParams.ParseSearch(Request, errors, "q", 100);
        var mine = QueryParams.ParseBool(Request, errors, "mine");
        var statusFilter = QueryParams.ParseEnum(Request, errors, "status", ArtifactStatuses.All);
        var sourceFilter = QueryParams.ParseEnum(Request, errors, "source", ArtifactSources.All);
        QueryParams.ThrowIfErrors(errors);

        IQueryable<Artifact> query = _db.Artifacts.WithDetails();
        if (mine)
        {
            query = query.Where(artifact => artifact.AuthorId == auth.User.Id);
        }
        else if (statusFilter is not null && auth.User.Role == UserRoles.SystemAdmin)
        {
            // Administrators maintain archived content, so an explicit status
            // filter replaces the default published-only scope for them.
            query = query.Where(artifact => artifact.Status == statusFilter);
        }
        else
        {
            query = query.Where(artifact => artifact.Status == ArtifactStatuses.Published);
            if (statusFilter is not null)
            {
                query = query.Where(artifact => artifact.Status == statusFilter);
            }
        }
        var search = q?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            var pattern = $"%{search}%";
            query = query.Where(artifact =>
                EF.Functions.Like(artifact.Title, pattern)
                || EF.Functions.Like(artifact.Summary, pattern));
        }
        if (sourceFilter is not null)
        {
            var sourceArtifactIds = _db.TaskSubmissions
                .Where(submission => sourceFilter == ArtifactSources.TaskResult
                    ? submission.Task!.CompetitionId == null
                    : submission.Task!.CompetitionId != null)
                .Select(submission => submission.ArtifactId);
            query = query.Where(artifact => sourceArtifactIds.Contains(artifact.Id));
        }

        var total = await query.CountAsync(cancellationToken);
        query = mine
            ? query
                .OrderByDescending(artifact => artifact.UpdatedAt)
                .ThenByDescending(artifact => artifact.Id)
            : query
                .OrderByDescending(artifact => artifact.PublishedAt)
                .ThenByDescending(artifact => artifact.Id);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return this.SnakeJson(new ArtifactListResponseDto(
            items.Select(ArtifactService.ToListItem).ToList(),
            page,
            pageSize,
            total));
    }

    /// <summary>Create an artifact draft with optional ordered attachments.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateArtifact(CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var payload = await ArtifactCreate.ParseAsync(Request);

        var now = DateTime.UtcNow;
        var artifact = new Artifact
        {
            Title = payload.Title,
            Summary = payload.Summary,
            ContentMarkdown = payload.ContentMarkdown,
            AuthorId = auth.User.Id,
            Author = auth.User,
            Status = ArtifactStatuses.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await ArtifactService.ReplaceArtifactFilesAsync(_db, artifact, payload.FileIds, auth.User);
        _db.Artifacts.Add(artifact);
        await _db.SaveChangesAsync(cancellationToken);
        return this.SnakeJson(ArtifactService.ToRead(artifact), statusCode: 201);
    }

    [HttpGet("{artifactId}")]
    public async Task<IActionResult> GetArtifact(string artifactId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "artifact_id", "artifactId");
        QueryParams.ThrowIfErrors(errors);
        var artifact = await _db.GetVisibleArtifactAsync(parsedId, auth.User);
        return this.SnakeJson(ArtifactService.ToRead(artifact));
    }

    /// <summary>Show which tasks reference this artifact; others see accepted rounds only.</summary>
    [HttpGet("{artifactId}/task-submissions")]
    public async Task<IActionResult> ListArtifactTaskSources(string artifactId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "artifact_id", "artifactId");
        QueryParams.ThrowIfErrors(errors);

        var artifact = await _db.GetVisibleArtifactAsync(parsedId, auth.User);
        var submissions = await _db.TaskSubmissions
            .WithDetails()
            .Where(submission => submission.ArtifactId == artifact.Id
                && (submission.Participant!.UserId == auth.User.Id
                    || submission.Status == SubmissionStatuses.Accepted))
            .OrderByDescending(submission => submission.SubmittedAt)
            .ThenByDescending(submission => submission.Id)
            .ToListAsync(cancellationToken);
        var items = new List<TaskSubmissionDto>();
        foreach (var submission in submissions)
        {
            items.Add(await _db.ToSubmissionDtoAsync(submission, includeTask: true, viewer: auth.User));
        }
        return this.SnakeJson(new ArtifactTaskSourceListResponseDto(items));
    }

    [HttpPatch("{artifactId}")]
    public async Task<IActionResult> UpdateArtifact(string artifactId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "artifact_id", "artifactId");
        var payload = await ArtifactUpdate.ParseAsync(Request);
        QueryParams.ThrowIfErrors(errors);

        var artifact = await _db.GetVisibleArtifactAsync(parsedId, auth.User);
        ArtifactService.RequireArtifactAuthor(artifact, auth.User);
        if (artifact.Status == ArtifactStatuses.Archived)
        {
            throw StateConflict("Restore an archived artifact before editing it");
        }

        if (payload.AnyExplicitNull)
        {
            throw new AppError("ARTIFACT_FIELD_REQUIRED", "Artifact fields cannot be null", 422);
        }
        var changed = false;
        if (payload.HasTitle)
        {
            artifact.Title = payload.Title!;
            changed = true;
        }
        if (payload.HasSummary)
        {
            artifact.Summary = payload.Summary!;
            changed = true;
        }
        if (payload.HasContentMarkdown)
        {
            artifact.ContentMarkdown = payload.ContentMarkdown!;
            changed = true;
        }
        if (payload.HasFileIds)
        {
            await ArtifactService.ReplaceArtifactFilesAsync(_db, artifact, payload.FileIds!, auth.User);
            changed = true;
        }
        if (changed)
        {
            artifact.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        return this.SnakeJson(ArtifactService.ToRead(artifact));
    }

    /// <summary>Delete an artifact draft.</summary>
    [HttpDelete("{artifactId}")]
    public async Task<IActionResult> DeleteArtifact(string artifactId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "artifact_id", "artifactId");
        QueryParams.ThrowIfErrors(errors);

        var artifact = await _db.GetVisibleArtifactAsync(parsedId, auth.User);
        ArtifactService.RequireArtifactAuthor(artifact, auth.User);
        if (artifact.Status != ArtifactStatuses.Draft)
        {
            throw StateConflict("Only a draft artifact can be deleted");
        }
        _db.Artifacts.Remove(artifact);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{artifactId}/publish")]
    public async Task<IActionResult> PublishArtifact(string artifactId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "artifact_id", "artifactId");
        QueryParams.ThrowIfErrors(errors);

        var artifact = await _db.GetVisibleArtifactAsync(parsedId, auth.User);
        ArtifactService.RequireArtifactAuthor(artifact, auth.User);
        if (artifact.Status != ArtifactStatuses.Draft)
        {
            throw StateConflict("Only a draft artifact can be published");
        }
        var now = DateTime.UtcNow;
        artifact.Status = ArtifactStatuses.Published;
        artifact.PublishedAt = now;
        artifact.ArchivedAt = null;
        artifact.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return this.SnakeJson(ArtifactService.ToRead(artifact));
    }

    [HttpPost("{artifactId}/archive")]
    public async Task<IActionResult> ArchiveArtifact(string artifactId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "artifact_id", "artifactId");
        QueryParams.ThrowIfErrors(errors);

        var artifact = await _db.GetVisibleArtifactAsync(parsedId, auth.User);
        ArtifactService.RequireAuthorOrAdmin(artifact, auth.User);
        if (artifact.Status != ArtifactStatuses.Published)
        {
            throw StateConflict("Only a published artifact can be archived");
        }
        var now = DateTime.UtcNow;
        artifact.Status = ArtifactStatuses.Archived;
        artifact.ArchivedAt = now;
        artifact.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return this.SnakeJson(ArtifactService.ToRead(artifact));
    }

    [HttpPost("{artifactId}/restore")]
    public async Task<IActionResult> RestoreArtifact(string artifactId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "artifact_id", "artifactId");
        QueryParams.ThrowIfErrors(errors);

        var artifact = await _db.GetVisibleArtifactAsync(parsedId, auth.User);
        ArtifactService.RequireAuthorOrAdmin(artifact, auth.User);
        if (artifact.Status != ArtifactStatuses.Archived)
        {
            throw StateConflict("Only an archived artifact can be restored");
        }
        artifact.Status = ArtifactStatuses.Published;
        artifact.ArchivedAt = null;
        artifact.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return this.SnakeJson(ArtifactService.ToRead(artifact));
    }
}

/// <summary>Artifact comment endpoints.</summary>
[Route("api/v1/artifacts/{artifactId}/comments")]
public sealed class ArtifactCommentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;

    public ArtifactCommentsController(AppDbContext db, Authenticator auth)
    {
        _db = db;
        _auth = auth;
    }

    /// <summary>Visible comments for one artifact, oldest first.</summary>
    [HttpGet]
    public async Task<IActionResult> ListComments(string artifactId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "artifact_id", "artifactId");
        var page = QueryParams.ParseInt(Request, errors, "page", 1, minimum: 1);
        var pageSize = QueryParams.ParseInt(Request, errors, "page_size", 50, minimum: 1, maximum: 100);
        QueryParams.ThrowIfErrors(errors);

        await _db.GetVisibleArtifactAsync(parsedId, auth.User);
        IQueryable<Comment> query = _db.Comments.Include(comment => comment.Author);
        query = query.Where(comment => comment.ArtifactId == parsedId);
        // Hidden comments stay visible to administrators so they can restore them.
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

    /// <summary>Comment on a published artifact.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateComment(string artifactId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var payload = await CommentCreate.ParseAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "artifact_id", "artifactId");
        QueryParams.ThrowIfErrors(errors);

        var artifact = await _db.GetVisibleArtifactAsync(parsedId, auth.User);
        if (artifact.Status != ArtifactStatuses.Published)
        {
            throw new AppError(
                "ARTIFACT_STATE_CONFLICT",
                "Only a published artifact can receive comments",
                409);
        }
        var now = DateTime.UtcNow;
        var comment = new Comment
        {
            ArtifactId = artifact.Id,
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

/// <summary>Deletion of one's own comment.</summary>
[Route("api/v1/comments")]
public sealed class CommentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;

    public CommentsController(AppDbContext db, Authenticator auth)
    {
        _db = db;
        _auth = auth;
    }

    [HttpDelete("{commentId}")]
    public async Task<IActionResult> DeleteComment(string commentId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "comment_id", "commentId");
        QueryParams.ThrowIfErrors(errors);

        var comment = await _db.Comments.FindAsync(new object[] { parsedId }, cancellationToken);
        if (comment is null)
        {
            throw new AppError("COMMENT_NOT_FOUND", "Comment not found", 404);
        }
        if (comment.AuthorId != auth.User.Id)
        {
            throw new AppError("FORBIDDEN", "Only the comment author can delete it", 403);
        }
        _db.Comments.Remove(comment);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
