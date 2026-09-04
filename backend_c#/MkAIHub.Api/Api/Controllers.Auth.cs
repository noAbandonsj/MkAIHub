using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Security;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>Authentication and current-user endpoints.</summary>
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Settings _settings;
    private readonly Authenticator _auth;

    public AuthController(AppDbContext db, Settings settings, Authenticator auth)
    {
        _db = db;
        _settings = settings;
        _auth = auth;
    }

    private AppError InvalidCredentials()
        => new("INVALID_CREDENTIALS", "Username or password is incorrect", 401);

    private void SetSessionCookie(string rawToken)
    {
        Response.Cookies.Append(
            _settings.SessionCookieName,
            rawToken,
            new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = _auth.CookieSecure(),
                MaxAge = TimeSpan.FromHours(_settings.SessionTtlHours),
            });
    }

    private void ClearSessionCookie()
    {
        Response.Cookies.Append(
            _settings.SessionCookieName,
            string.Empty,
            new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = _auth.CookieSecure(),
                Expires = DateTimeOffset.UnixEpoch,
            });
    }

    /// <summary>Authenticate a user and issue a new server-side session cookie.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login(CancellationToken cancellationToken)
    {
        var payload = await LoginRequest.ParseAsync(Request);
        var user = await _db.Users.SingleOrDefaultAsync(
            item => item.Username == payload.Username, cancellationToken);
        var passwordOk = user is not null && PasswordHasher.VerifyPassword(payload.Password, user.PasswordHash);
        if (user is null || !passwordOk || !user.IsActive)
        {
            throw InvalidCredentials();
        }

        var now = DateTime.UtcNow;
        user.LastLoginAt = now;
        // SQLAlchemy's onupdate refreshes updated_at whenever the row changes.
        user.UpdatedAt = now;
        var (rawToken, session) = SessionService.CreateSession(user, _settings.SessionTtlHours);
        _db.UserSessions.Add(session);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new AppError("AUTHENTICATION_FAILED", "Unable to create a session", 503);
        }

        SetSessionCookie(rawToken);
        return this.SnakeJson(new LoginResponseDto(ArtifactService.ToUserRead(user)));
    }

    /// <summary>Revoke the current session and clear the browser cookie.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        SessionService.RevokeSession(auth.Session);
        await _db.SaveChangesAsync(cancellationToken);
        ClearSessionCookie();
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        return this.SnakeJson(ArtifactService.ToUserRead(auth.User));
    }

    [HttpGet("csrf-token")]
    public async Task<IActionResult> CsrfToken()
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        return this.SnakeJson(new CsrfTokenResponseDto(_auth.DeriveCsrfToken(auth.RawToken)));
    }

    /// <summary>Change the current user's password and revoke every session.</summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var payload = await ChangePasswordRequest.ParseAsync(Request);
        if (!PasswordHasher.VerifyPassword(payload.CurrentPassword, auth.User.PasswordHash))
        {
            throw new AppError("CURRENT_PASSWORD_INVALID", "Current password is incorrect", 400);
        }
        if (payload.CurrentPassword == payload.NewPassword)
        {
            throw new AppError("PASSWORD_UNCHANGED", "New password must differ from the current password", 400);
        }

        auth.User.PasswordHash = PasswordHasher.HashPassword(payload.NewPassword);
        auth.User.UpdatedAt = DateTime.UtcNow;
        SessionService.RevokeAllSessions(_db, auth.User.Id);
        await _db.SaveChangesAsync(cancellationToken);
        ClearSessionCookie();
        return NoContent();
    }
}
