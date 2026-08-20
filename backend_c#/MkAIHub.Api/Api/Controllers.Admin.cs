using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Security;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>System-administrator user-management endpoints.</summary>
[Route("api/v1/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;

    public AdminController(AppDbContext db, Authenticator auth)
    {
        _db = db;
        _auth = auth;
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

        var changed = false;
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
            changed = true;
        }
        if (payload.HasRole && payload.Role is not null)
        {
            user.Role = payload.Role;
            changed = true;
        }
        if (payload.HasIsActive && payload.IsActive is not null)
        {
            user.IsActive = payload.IsActive.Value;
            changed = true;
            if (!user.IsActive)
            {
                SessionService.RevokeAllSessions(_db, user.Id);
            }
        }
        if (!changed)
        {
            return this.SnakeJson(ArtifactService.ToUserRead(user));
        }
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
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
        return NoContent();
    }
}
