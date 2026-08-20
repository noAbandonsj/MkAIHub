using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Data;
using MkAIHub.Api.Security;

namespace MkAIHub.Api.Services;

/// <summary>
/// Creation, lookup support, and revocation of server-side sessions.
/// Mirrors app.services.sessions.
/// </summary>
public static class SessionService
{
    public const int SessionTokenBytes = 32;

    public static (string RawToken, UserSession Session) CreateSession(User user, int ttlHours)
    {
        // High-entropy raw token; only its SHA-256 hash is stored.
        var rawToken = Base64UrlTextEncoder.Encode(RandomNumberGenerator.GetBytes(SessionTokenBytes));
        var now = DateTime.UtcNow;
        var session = new UserSession
        {
            UserId = user.Id,
            TokenHash = PasswordHasher.HashSessionToken(rawToken),
            ExpiresAt = now.AddHours(ttlHours),
            LastSeenAt = now,
            CreatedAt = now,
        };
        return (rawToken, session);
    }

    public static void RevokeSession(UserSession session, DateTime? at = null)
        => session.RevokedAt = at ?? DateTime.UtcNow;

    /// <summary>Revoke every currently active session belonging to a user.</summary>
    public static int RevokeAllSessions(AppDbContext db, int userId, DateTime? at = null)
    {
        var resolvedAt = at ?? DateTime.UtcNow;
        var timestamp = Database.FormatSqliteTimestamp(resolvedAt);
        return db.Database.ExecuteSqlInterpolated(
            $"UPDATE user_sessions SET revoked_at = {timestamp} WHERE user_id = {userId} AND revoked_at IS NULL");
    }
}
