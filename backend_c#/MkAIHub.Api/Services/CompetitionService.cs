using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Api;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;

namespace MkAIHub.Api.Services;

/// <summary>
/// Competition query, computed status, and response helpers.
/// Mirrors app.services.competitions.
/// </summary>
public static class CompetitionService
{
    /// <summary>Compute UPCOMING/ONGOING/ENDED from the server clock, never stored.</summary>
    public static string CompetitionStatus(Competition competition, DateTime? now = null)
    {
        var current = now ?? DateTime.UtcNow;
        if (current < competition.StartAt)
        {
            return CompetitionStatuses.Upcoming;
        }
        if (current < competition.EndAt)
        {
            return CompetitionStatuses.Ongoing;
        }
        return CompetitionStatuses.Ended;
    }

    public static IQueryable<Competition> WithCreator(this IQueryable<Competition> query)
        => query.Include(competition => competition.Creator);

    public static async Task<Competition> GetCompetitionAsync(this AppDbContext db, int competitionId)
    {
        var competition = await db.Competitions
            .WithCreator()
            .FirstOrDefaultAsync(item => item.Id == competitionId);
        if (competition is null)
        {
            throw new AppError("COMPETITION_NOT_FOUND", "Competition not found", 404);
        }
        return competition;
    }

    public static CompetitionListItemDto ToListItem(Competition competition)
        => new(
            competition.Id,
            competition.Title,
            competition.Summary,
            ArtifactService.ToSummary(competition.Creator!),
            CompetitionStatus(competition),
            competition.StartAt,
            competition.EndAt,
            competition.CreatedAt,
            competition.UpdatedAt);

    public static CompetitionReadDto ToRead(Competition competition)
        => new(
            competition.Id,
            competition.Title,
            competition.Summary,
            ArtifactService.ToSummary(competition.Creator!),
            CompetitionStatus(competition),
            competition.StartAt,
            competition.EndAt,
            competition.CreatedAt,
            competition.UpdatedAt,
            competition.RulesMarkdown);
}
