using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Api;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;

namespace MkAIHub.Api.Services;

/// <summary>
/// Issue query, authorization, and response helpers.
/// Mirrors app.services.issues.
/// </summary>
public static class IssueService
{
    public static IQueryable<Issue> WithAuthor(this IQueryable<Issue> query)
        => query.Include(issue => issue.Author);

    public static async Task<Issue> GetIssueAsync(this AppDbContext db, int issueId)
    {
        var issue = await db.Issues
            .WithAuthor()
            .FirstOrDefaultAsync(item => item.Id == issueId);
        if (issue is null)
        {
            throw new AppError("ISSUE_NOT_FOUND", "Issue not found", 404);
        }
        return issue;
    }

    public static void RequireIssueAuthor(Issue issue, User user)
    {
        if (issue.AuthorId != user.Id)
        {
            throw new AppError("FORBIDDEN", "Only the issue author can edit this issue", 403);
        }
    }

    public static void RequireIssueAuthorOrAdmin(Issue issue, User user)
    {
        if (issue.AuthorId != user.Id && user.Role != UserRoles.SystemAdmin)
        {
            throw new AppError("FORBIDDEN", "Issue permission is required", 403);
        }
    }

    public static IssueListItemDto ToListItem(Issue issue)
        => new(
            issue.Id,
            issue.Title,
            ArtifactService.ToSummary(issue.Author!),
            issue.Status,
            issue.ClosedAt,
            issue.CreatedAt,
            issue.UpdatedAt);

    public static IssueReadDto ToRead(Issue issue)
        => new(
            issue.Id,
            issue.Title,
            ArtifactService.ToSummary(issue.Author!),
            issue.Status,
            issue.ClosedAt,
            issue.CreatedAt,
            issue.UpdatedAt,
            issue.Description);
}
