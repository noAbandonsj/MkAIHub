using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Api;
using MkAIHub.Api.Data;

namespace MkAIHub.Api.Services;

/// <summary>
/// Read-only aggregates for the personal workbench.
/// Mirrors app.services.workbench.
/// </summary>
public static class WorkbenchService
{
    public static async Task<WorkbenchResponseDto> BuildResponseAsync(AppDbContext db, User user)
    {
        var participatedTasks = await db.TaskParticipants
            .Where(participant => participant.UserId == user.Id
                && participant.Status == TaskClosureService.Active)
            .Select(participant => participant.TaskId)
            .Distinct()
            .CountAsync();
        var competitionTasks = await db.TaskParticipants
            .Where(participant => participant.UserId == user.Id
                && participant.Status == TaskClosureService.Active)
            .Join(
                db.Tasks,
                participant => participant.TaskId,
                task => task.Id,
                (participant, task) => task.CompetitionId)
            .Where(competitionId => competitionId != null)
            .Distinct()
            .CountAsync();
        var pendingTaskReviews = await db.TaskSubmissions
            .Join(
                db.Tasks,
                submission => submission.TaskId,
                task => task.Id,
                (submission, task) => new { submission, task })
            .Where(row => row.task.CreatorId == user.Id
                && row.task.CompetitionId == null
                && row.submission.IsCurrent
                && TaskClosureService.PendingSubmissionStatuses.Contains(row.submission.Status))
            .CountAsync();

        var pendingCompetitionReviews = 0;
        if (user.Role == UserRoles.SystemAdmin)
        {
            pendingCompetitionReviews = await db.TaskSubmissions
                .Join(
                    db.Tasks,
                    submission => submission.TaskId,
                    task => task.Id,
                    (submission, task) => new { submission, task })
                .Join(
                    db.Competitions,
                    row => row.task.CompetitionId,
                    competition => competition.Id,
                    (row, competition) => new { row.submission, competition })
                .GroupJoin(
                    db.CompetitionReviews,
                    row => row.submission.Id,
                    review => review.TaskSubmissionId,
                    (row, reviews) => new { row.submission, row.competition, reviews })
                .SelectMany(
                    row => row.reviews.DefaultIfEmpty(),
                    (row, review) => new { row.submission, row.competition, review })
                .Where(row => row.submission.IsCurrent
                    && row.competition.Status == CompetitionLifecycles.Published
                    && row.review == null)
                .CountAsync();
        }

        var statistics = new ClosureStatisticsDto(
            Participations: await db.TaskParticipants.CountAsync(
                participant => participant.Status == TaskClosureService.Active),
            Submissions: await db.TaskSubmissions.CountAsync(),
            AcceptedSubmissions: await db.TaskSubmissions.CountAsync(
                submission => submission.Status == TaskClosureService.Accepted),
            CompetitionTaskCompletions: await db.TaskSubmissions
                .Join(
                    db.Tasks,
                    submission => submission.TaskId,
                    task => task.Id,
                    (submission, task) => new { submission, task })
                .Where(row => row.task.CompetitionId != null && row.submission.IsCurrent)
                .CountAsync(),
            PublishedResults: await db.CompetitionResults
                .Select(result => result.CompetitionId)
                .Distinct()
                .CountAsync());
        return new WorkbenchResponseDto(
            new WorkbenchCountsDto(
                participatedTasks,
                competitionTasks,
                pendingTaskReviews,
                pendingCompetitionReviews),
            statistics);
    }
}
