using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Security;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>System-administrator user and comment management endpoints.</summary>
[Route("api/v1/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;
    private readonly ILogger _logger;

    public AdminController(AppDbContext db, Authenticator auth, ILoggerFactory loggerFactory)
    {
        _db = db;
        _auth = auth;
        // The audit logger mirrors Python's "mkaihub" application logger.
        _logger = loggerFactory.CreateLogger("mkaihub");
    }

    private static AppError UserNotFound()
        => new("USER_NOT_FOUND", "User not found", 404);

    /// <summary>Return a bounded, searchable user list for administrators.</summary>
    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdmin(await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var page = QueryParams.ParseInt(Request, errors, "page", 1, minimum: 1);
        var pageSize = QueryParams.ParseInt(Request, errors, "page_size", 20, minimum: 1, maximum: 100);
        var q = QueryParams.ParseSearch(Request, errors, "q", 100);
        QueryParams.ThrowIfErrors(errors);

        IQueryable<User> users = _db.Users;
        var search = q?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            var pattern = $"%{search}%";
            users = users.Where(user =>
                EF.Functions.Like(user.Username, pattern)
                || EF.Functions.Like(user.DisplayName, pattern));
        }

        var total = await users.CountAsync(cancellationToken);
        var items = await users
            .OrderBy(user => user.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return this.SnakeJson(new UserListResponseDto(
            items.Select(ArtifactService.ToUserRead).ToList(),
            page,
            pageSize,
            total));
    }

    /// <summary>Create an employee or another system administrator.</summary>
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(CancellationToken cancellationToken)
    {
        var payload = await AdminUserCreate.ParseAsync(Request);
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));

        var existing = await _db.Users
            .Where(user => user.Username == payload.Username)
            .Select(user => user.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing != 0)
        {
            throw new AppError("USERNAME_TAKEN", "Username is already in use", 409);
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Username = payload.Username,
            DisplayName = payload.DisplayName,
            PasswordHash = PasswordHasher.HashPassword(payload.Password),
            Role = payload.Role,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new AppError("USERNAME_TAKEN", "Username is already in use", 409);
        }
        _logger.LogAdminAction(
            "user.create",
            auth.User.Id,
            "user",
            user.Id,
            new Dictionary<string, object?>
            {
                ["username"] = user.Username,
                ["role"] = user.Role,
            });
        return this.SnakeJson(ArtifactService.ToUserRead(user), statusCode: 201);
    }

    /// <summary>Update mutable user fields while protecting the current administrator.</summary>
    [HttpPatch("users/{userId}")]
    public async Task<IActionResult> UpdateUser(string userId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedUserId = QueryParams.ParsePathInt(RouteData.Values, errors, "user_id", "userId");
        var payload = await AdminUserPatch.ParseAsync(Request);
        QueryParams.ThrowIfErrors(errors);

        var user = await _db.Users.FindAsync(new object[] { parsedUserId }, cancellationToken);
        if (user is null)
        {
            throw UserNotFound();
        }

        // Presence-driven change set, matching pydantic's exclude_unset semantics.
        var fields = new List<string>();
        if (payload.HasDisplayName)
        {
            fields.Add("display_name");
        }
        if (payload.HasRole)
        {
            fields.Add("role");
        }
        if (payload.HasIsActive)
        {
            fields.Add("is_active");
        }
        if (fields.Count == 0)
        {
            return this.SnakeJson(ArtifactService.ToUserRead(user));
        }
        if (user.Id == auth.User.Id
            && ((payload.HasIsActive && payload.IsActive == false)
                || (payload.HasRole && payload.Role == UserRoles.Employee)))
        {
            throw new AppError(
                "SELF_ADMIN_PROTECTED",
                "An administrator cannot deactivate or downgrade their own account",
                403);
        }

        if (payload.HasDisplayName)
        {
            user.DisplayName = payload.DisplayName!;
        }
        if (payload.HasRole && payload.Role is not null)
        {
            user.Role = payload.Role;
        }
        if (payload.HasIsActive && payload.IsActive is not null)
        {
            user.IsActive = payload.IsActive.Value;
            if (!user.IsActive)
            {
                SessionService.RevokeAllSessions(_db, user.Id);
            }
        }
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        fields.Sort(StringComparer.Ordinal);
        _logger.LogAdminAction(
            "user.update",
            auth.User.Id,
            "user",
            user.Id,
            new Dictionary<string, object?>
            {
                ["username"] = user.Username,
                ["fields"] = fields,
            });
        return this.SnakeJson(ArtifactService.ToUserRead(user));
    }

    /// <summary>Reset a user's password and revoke every session for that user.</summary>
    [HttpPost("users/{userId}/reset-password")]
    public async Task<IActionResult> ResetPassword(string userId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedUserId = QueryParams.ParsePathInt(RouteData.Values, errors, "user_id", "userId");
        var payload = await AdminResetPassword.ParseAsync(Request);
        QueryParams.ThrowIfErrors(errors);

        var user = await _db.Users.FindAsync(new object[] { parsedUserId }, cancellationToken);
        if (user is null)
        {
            throw UserNotFound();
        }
        user.PasswordHash = PasswordHasher.HashPassword(payload.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        SessionService.RevokeAllSessions(_db, user.Id);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogAdminAction(
            "user.reset_password",
            auth.User.Id,
            "user",
            user.Id,
            new Dictionary<string, object?> { ["username"] = user.Username });
        return NoContent();
    }

    private static async Task<Comment> GetCommentAsync(AppDbContext db, int commentId)
    {
        var comment = await db.Comments.FindAsync(new object[] { commentId });
        if (comment is null)
        {
            throw new AppError("COMMENT_NOT_FOUND", "Comment not found", 404);
        }
        return comment;
    }

    private static async Task SetCommentStatusAsync(
        AppDbContext db,
        Comment comment,
        string targetStatus,
        CancellationToken cancellationToken)
    {
        if (comment.Status != targetStatus)
        {
            comment.Status = targetStatus;
            comment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Hide an inappropriate comment from non-administrator employees.</summary>
    [HttpPost("comments/{commentId}/hide")]
    public async Task<IActionResult> HideComment(string commentId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "comment_id", "commentId");
        QueryParams.ThrowIfErrors(errors);

        var comment = await GetCommentAsync(_db, parsedId);
        await SetCommentStatusAsync(_db, comment, CommentStatuses.Hidden, cancellationToken);
        _logger.LogAdminAction("comment.hide", auth.User.Id, "comment", comment.Id);
        return NoContent();
    }

    /// <summary>Make a previously hidden comment visible again.</summary>
    [HttpPost("comments/{commentId}/restore")]
    public async Task<IActionResult> RestoreComment(string commentId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "comment_id", "commentId");
        QueryParams.ThrowIfErrors(errors);

        var comment = await GetCommentAsync(_db, parsedId);
        await SetCommentStatusAsync(_db, comment, CommentStatuses.Visible, cancellationToken);
        _logger.LogAdminAction("comment.restore", auth.User.Id, "comment", comment.Id);
        return NoContent();
    }

    /// <summary>Reopen a closed task for correction; completed tasks stay terminal.</summary>
    [HttpPost("tasks/{taskId}/reopen")]
    public async Task<IActionResult> ReopenTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId, auth.User);
        await _db.ReopenTaskAsync(task);
        _logger.LogAdminAction(
            "task.reopen",
            auth.User.Id,
            "task",
            task.Id,
            new Dictionary<string, object?> { ["status"] = task.Status });
        return this.SnakeJson(await _db.ToReadAsync(task, auth.User));
    }
}
