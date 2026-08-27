using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Api;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;

namespace MkAIHub.Api.Services;

/// <summary>
/// Artifact query, authorization, and response helpers.
/// Mirrors app.services.artifacts.
/// </summary>
public static class ArtifactService
{
    public static IQueryable<Artifact> WithDetails(this IQueryable<Artifact> query)
        => query
            .Include(artifact => artifact.Author)
            .Include(artifact => artifact.FileLinks)
                .ThenInclude(link => link.File)
            .Include(artifact => artifact.TaskSubmissions)
                .ThenInclude(submission => submission.Task);

    public static async Task<Artifact> GetVisibleArtifactAsync(this AppDbContext db, int artifactId, User user)
    {
        var artifact = await db.Artifacts
            .WithDetails()
            .FirstOrDefaultAsync(item => item.Id == artifactId);
        if (artifact is null)
        {
            throw ArtifactNotFound();
        }
        if (artifact.Status != ArtifactStatuses.Published
            && artifact.AuthorId != user.Id
            && user.Role != UserRoles.SystemAdmin)
        {
            throw ArtifactNotFound();
        }
        return artifact;
    }

    public static AppError ArtifactNotFound()
        => new("ARTIFACT_NOT_FOUND", "Artifact not found", 404);

    public static void RequireArtifactAuthor(Artifact artifact, User user)
    {
        if (artifact.AuthorId != user.Id)
        {
            throw new AppError("FORBIDDEN", "Only the artifact author can edit this content", 403);
        }
    }

    public static void RequireAuthorOrAdmin(Artifact artifact, User user)
    {
        if (artifact.AuthorId != user.Id && user.Role != UserRoles.SystemAdmin)
        {
            throw new AppError("FORBIDDEN", "Artifact permission is required", 403);
        }
    }

    /// <summary>Replace the ordered attachment list after validating ownership.</summary>
    public static async Task ReplaceArtifactFilesAsync(
        AppDbContext db,
        Artifact artifact,
        IReadOnlyList<int> fileIds,
        User user)
    {
        var files = fileIds.Count == 0
            ? new List<StoredFile>()
            : await db.Files
                .Where(file => fileIds.Contains(file.Id) && !file.IsDeleted)
                .ToListAsync();
        var byId = files.ToDictionary(file => file.Id);
        if (byId.Count != fileIds.Count)
        {
            throw new AppError("FILE_NOT_FOUND", "One or more files were not found", 404);
        }
        if (files.Any(file => file.UploaderId != user.Id))
        {
            throw new AppError("FILE_FORBIDDEN", "Only your own uploads can be attached", 403);
        }

        foreach (var link in artifact.FileLinks.ToList())
        {
            db.ArtifactFiles.Remove(link);
        }
        artifact.FileLinks.Clear();
        var sortOrder = 0;
        foreach (var fileId in fileIds)
        {
            var link = new ArtifactFile
            {
                Artifact = artifact,
                File = byId[fileId],
                SortOrder = sortOrder,
            };
            artifact.FileLinks.Add(link);
            sortOrder += 1;
        }
    }

    public static ArtifactListItemDto ToListItem(Artifact artifact)
        => new(
            artifact.Id,
            artifact.Title,
            artifact.Summary,
            ToSummary(artifact.Author!),
            artifact.Status,
            artifact.FileLinks.Count,
            SourceTypes(artifact),
            artifact.PublishedAt,
            artifact.CreatedAt,
            artifact.UpdatedAt);

    /// <summary>Derive TASK_RESULT / COMPETITION_ENTRY badges from submission links.</summary>
    public static IReadOnlyList<string> SourceTypes(Artifact artifact)
    {
        var sourceTypes = new List<string>();
        if (artifact.TaskSubmissions.Any(submission => submission.Task?.CompetitionId == null))
        {
            sourceTypes.Add(ArtifactSources.TaskResult);
        }
        if (artifact.TaskSubmissions.Any(submission => submission.Task?.CompetitionId != null))
        {
            sourceTypes.Add(ArtifactSources.CompetitionEntry);
        }
        return sourceTypes;
    }

    public static ArtifactReadDto ToRead(Artifact artifact)
        => new(
            artifact.Id,
            artifact.Title,
            artifact.Summary,
            ToSummary(artifact.Author!),
            artifact.Status,
            artifact.FileLinks.Count,
            SourceTypes(artifact),
            artifact.PublishedAt,
            artifact.CreatedAt,
            artifact.UpdatedAt,
            artifact.ContentMarkdown,
            artifact.ArchivedAt,
            artifact.FileLinks
                .Where(link => !link.File!.IsDeleted)
                .OrderBy(link => link.SortOrder)
                .Select(link => ToFileRead(link.File!))
                .ToList());

    public static FileReadDto ToFileRead(StoredFile file)
        => new(
            file.Id,
            file.OriginalName,
            file.Extension,
            file.MimeType,
            file.SizeBytes,
            file.UploaderId,
            file.CreatedAt);

    public static UserSummaryDto ToSummary(User user)
        => new(user.Id, user.Username, user.DisplayName);

    public static UserReadDto ToUserRead(User user)
        => new(
            user.Id,
            user.Username,
            user.DisplayName,
            user.Role,
            user.IsActive,
            user.LastLoginAt,
            user.CreatedAt,
            user.UpdatedAt);

    public static CommentReadDto ToCommentRead(Comment comment)
        => new(
            comment.Id,
            comment.ArtifactId,
            comment.IssueId,
            ToSummary(comment.Author!),
            comment.Content,
            comment.Status,
            comment.CreatedAt,
            comment.UpdatedAt);
}
