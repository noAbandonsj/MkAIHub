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
    public static IQueryable<TaskItem> WithCreator(this IQueryable<TaskItem> query)
        => query.Include(task => task.Creator);

    public static async Task<TaskItem> GetTaskAsync(this AppDbContext db, int taskId)
    {
        var task = await db.Tasks
            .WithCreator()
            .FirstOrDefaultAsync(item => item.Id == taskId);
        if (task is null)
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
            task.DeadlineAt,
            task.CompletedAt,
            task.ClosedAt,
            task.CreatedAt,
            task.UpdatedAt);

    public static TaskReadDto ToRead(TaskItem task)
        => new(
            task.Id,
            task.Title,
            ArtifactService.ToSummary(task.Creator!),
            task.Status,
            task.DeadlineAt,
            task.CompletedAt,
            task.ClosedAt,
            task.CreatedAt,
            task.UpdatedAt,
            task.Description);
}
