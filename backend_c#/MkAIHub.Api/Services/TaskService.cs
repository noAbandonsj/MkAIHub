using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Api;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;

namespace MkAIHub.Api.Services;

/// <summary>
/// Task query, authorization, and response helpers.
/// Mirrors app.services.tasks.
/// </summary>
public static class TaskService
{
    public static readonly IReadOnlyList<string> TerminalStatuses = new[]
    {
        TaskStatuses.Completed,
        TaskStatuses.Closed,
    };

    public static IQueryable<TaskItem> WithCreator(this IQueryable<TaskItem> query)
        => query
            .Include(task => task.Creator)
            .Include(task => task.Competition);

    /// <summary>Load a task; tasks under DRAFT competitions stay invisible to employees.</summary>
    public static async Task<TaskItem> GetTaskAsync(this AppDbContext db, int taskId, User? viewer = null)
    {
        var task = await db.Tasks
            .WithCreator()
            .FirstOrDefaultAsync(item => item.Id == taskId);
        if (task is null)
        {
            throw new AppError("TASK_NOT_FOUND", "Task not found", 404);
        }
        if (viewer is not null
            && task.CompetitionId is not null
            && viewer.Role != UserRoles.SystemAdmin
            && (task.Competition is null || task.Competition.Status == CompetitionLifecycles.Draft))
        {
            throw new AppError("TASK_NOT_FOUND", "Task not found", 404);
        }
        return task;
    }

    public static void RequireTaskCreator(TaskItem task, User user)
    {
        if (task.CreatorId != user.Id)
        {
            throw new AppError("FORBIDDEN", "Only the task creator can edit this task", 403);
        }
    }

    public static void RequireTaskCreatorOrAdmin(TaskItem task, User user)
    {
        if (task.CreatorId != user.Id && user.Role != UserRoles.SystemAdmin)
        {
            throw new AppError("FORBIDDEN", "Task permission is required", 403);
        }
    }

    public static TaskListItemDto ToListItem(TaskItem task)
        => new(
            task.Id,
            task.Title,
            ArtifactService.ToSummary(task.Creator!),
            task.Status,
            task.CompetitionId,
            task.Competition?.Title,
            task.DeadlineAt,
            task.CompletedAt,
            task.ClosedAt,
            task.CreatedAt,
            task.UpdatedAt);

    public static async Task<TaskReadDto> ToReadAsync(this AppDbContext db, TaskItem task, User? viewer = null)
    {
        var participantCount = await db.TaskParticipants
            .CountAsync(participant => participant.TaskId == task.Id && participant.Status == ParticipantStatuses.Active);
        var submissionCount = await db.TaskSubmissions
            .CountAsync(submission => submission.TaskId == task.Id);
        TaskParticipantDto? myParticipation = null;
        if (viewer is not null)
        {
            var participant = await db.TaskParticipants
                .Include(participant => participant.User)
                .FirstOrDefaultAsync(participant =>
                    participant.TaskId == task.Id && participant.UserId == viewer.Id);
            if (participant is not null)
            {
                myParticipation = TaskClosureService.ToParticipantDto(participant);
            }
        }
        var item = ToListItem(task);
        return new TaskReadDto(
            item.Id,
            item.Title,
            item.Creator,
            item.Status,
            item.CompetitionId,
            item.CompetitionTitle,
            item.DeadlineAt,
            item.CompletedAt,
            item.ClosedAt,
            item.CreatedAt,
            item.UpdatedAt,
            task.Description,
            myParticipation,
            participantCount,
            submissionCount);
    }
}
