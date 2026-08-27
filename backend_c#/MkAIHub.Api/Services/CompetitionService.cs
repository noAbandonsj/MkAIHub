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
        if (current < Database.AsUtc(competition.StartAt))
        {
            return CompetitionStatuses.Upcoming;
        }
        if (current < Database.AsUtc(competition.EndAt))
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
            competition.Status,
            competition.StartAt,
            competition.EndAt,
            competition.CreatedAt,
            competition.UpdatedAt);

    public static async Task<CompetitionReadDto> ToReadAsync(
        this AppDbContext db, Competition competition, User? viewer = null)
    {
        var taskCount = await db.Tasks.CountAsync(task => task.CompetitionId == competition.Id);
        var registrationCount = await db.CompetitionRegistrations.CountAsync(registration =>
            registration.CompetitionId == competition.Id
            && registration.Status == RegistrationStatuses.Registered);
        CompetitionRegistrationSummaryDto? myRegistration = null;
        if (viewer is not null)
        {
            var registration = await db.CompetitionRegistrations.FirstOrDefaultAsync(item =>
                item.CompetitionId == competition.Id && item.UserId == viewer.Id);
            if (registration is not null)
            {
                myRegistration = new CompetitionRegistrationSummaryDto(
                    registration.Id,
                    registration.Status,
                    registration.RegisteredAt,
                    registration.CancelledAt);
            }
        }
        var resultsPublished = await db.CompetitionResults.AnyAsync(result =>
            result.CompetitionId == competition.Id);
        var item = ToListItem(competition);
        return new CompetitionReadDto(
            item.Id,
            item.Title,
            item.Summary,
            item.Creator,
            item.Status,
            item.LifecycleStatus,
            item.StartAt,
            item.EndAt,
            item.CreatedAt,
            item.UpdatedAt,
            competition.RulesMarkdown,
            taskCount,
            registrationCount,
            myRegistration,
            resultsPublished);
    }
}
