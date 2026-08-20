using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Security;

namespace MkAIHub.Api.Api;

/// <summary>Authenticated request context containing the user and current session.</summary>
public sealed record AuthContext(User User, UserSession Session, string RawToken);

/// <summary>
/// The per-process CSRF signing secret, derived from APP_SECRET_KEY or freshly
/// generated for development/test processes.
/// </summary>
public sealed record CsrfSecret(byte[] Value);

/// <summary>
/// Cookie-session authentication, CSRF, and role checks.
/// Mirrors app.api.v1.deps.
/// </summary>
public sealed class Authenticator
{
    public const int SessionLastSeenThrottleSeconds = 300;

    private readonly AppDbContext _db;
    private readonly Settings _settings;
    private readonly CsrfSecret _csrfSecret;

    public Authenticator(AppDbContext db, Settings settings, CsrfSecret csrfSecret)
    {
        _db = db;
        _settings = settings;
        _csrfSecret = csrfSecret;
    }

    /// <summary>Resolve and validate the HttpOnly session cookie.</summary>
    public async Task<AuthContext> GetCurrentAuthAsync(HttpRequest request)
    {
        var rawToken = request.Cookies[_settings.SessionCookieName];
        if (string.IsNullOrEmpty(rawToken))
        {
            throw InvalidSession();
        }

        var tokenHash = PasswordHasher.HashSessionToken(rawToken);
        var session = await _db.UserSessions
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash);
        var now = DateTime.UtcNow;
        if (session is null || session.RevokedAt is not null)
        {
            throw InvalidSession();
        }
        if (Database.AsUtc(session.ExpiresAt) <= now)
        {
            throw InvalidSession();
        }
        var user = session.User;
        if (user is null || !user.IsActive)
        {
            throw InvalidSession();
        }

        // last_seen is persisted at most every five minutes to keep authenticated
        // read traffic from becoming a write workload.
        if ((now - Database.AsUtc(session.LastSeenAt)).TotalSeconds >= SessionLastSeenThrottleSeconds)
        {
            session.LastSeenAt = now;
            _db.SaveChanges();
        }

        return new AuthContext(user, session, rawToken);
    }

    public AppError InvalidSession()
        => new("AUTHENTICATION_REQUIRED", "Authentication is required", 401);

    /// <summary>Derive a session-bound CSRF token without storing another secret.</summary>
    public string DeriveCsrfToken(string rawToken)
    {
        using var hmac = new HMACSHA256(_csrfSecret.Value);
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>Require the double-submit CSRF header for an authenticated write.</summary>
    public AuthContext RequireCsrf(HttpRequest request, AuthContext auth)
    {
        var supplied = request.Headers["X-CSRF-Token"].FirstOrDefault() ?? string.Empty;
        var expected = DeriveCsrfToken(auth.RawToken);
        if (supplied.Length == 0
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(supplied),
                Encoding.UTF8.GetBytes(expected)))
        {
            throw new AppError("CSRF_INVALID", "CSRF token is missing or invalid", 403);
        }
        return auth;
    }

    /// <summary>Require the system administrator role.</summary>
    public AuthContext RequireAdmin(AuthContext auth)
    {
        if (auth.User.Role != UserRoles.SystemAdmin)
        {
            throw new AppError("FORBIDDEN", "System administrator permission is required", 403);
        }
        return auth;
    }

    public AuthContext RequireAdminCsrf(HttpRequest request, AuthContext auth)
        => RequireCsrf(request, RequireAdmin(auth));

    /// <summary>Use Secure cookies in production while keeping local HTTP development usable.</summary>
    public bool CookieSecure() => _settings.IsProduction;
}
