using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>Task CRUD and state-action endpoints.</summary>
[Route("api/v1/tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;

    public TasksController(AppDbContext db, Authenticator auth)
    {
        _db = db;
        _auth = auth;
    }

    private static AppError StateConflict(string message)
        => new("TASK_STATE_CONFLICT", message, 409);

    /// <summary>Filterable, paginated task list.</summary>
    [HttpGet]
    public async Task<IActionResult> ListTasks(CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var page = QueryParams.ParseInt(Request, errors, "page", 1, minimum: 1);
        var pageSize = QueryParams.ParseInt(Request, errors, "page_size", 12, minimum: 1, maximum: 100);
        var q = QueryParams.ParseSearch(Request, errors, "q", 100);
        var mine = QueryParams.ParseBool(Request, errors, "mine");
        var statusFilter = QueryParams.ParseEnum(Request, errors, "status", TaskStatuses.All);
        QueryParams.ThrowIfErrors(errors);

        IQueryable<TaskItem> query = _db.Tasks.WithCreator();
        if (mine)
        {
            query = query.Where(task => task.CreatorId == auth.User.Id);
        }
        if (statusFilter is not null)
        {
            query = query.Where(task => task.Status == statusFilter);
        }
        var search = q?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            var pattern = $"%{search}%";
            query = query.Where(task =>
                EF.Functions.Like(task.Title, pattern)
                || EF.Functions.Like(task.Description, pattern));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(task => task.UpdatedAt)
            .ThenByDescending(task => task.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return this.SnakeJson(new TaskListResponseDto(
            items.Select(TaskService.ToListItem).ToList(),
            page,
            pageSize,
            total));
    }

    /// <summary>Create a task owned by the current user.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateTask(CancellationToken cancellationToken)
    {
        var payload = await TaskCreate.ParseAsync(Request);
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));

        var now = DateTime.UtcNow;
        var task = new TaskItem
        {
            Title = payload.Title,
            Description = payload.Description,
            DeadlineAt = payload.DeadlineAt,
            CreatorId = auth.User.Id,
            Creator = auth.User,
            Status = TaskStatuses.Open,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(cancellationToken);
        var created = await _db.GetTaskAsync(task.Id);
        return this.SnakeJson(TaskService.ToRead(created), statusCode: 201);
    }

    [HttpGet("{taskId}")]
    public async Task<IActionResult> GetTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId);
        return this.SnakeJson(TaskService.ToRead(task));
    }

    /// <summary>Update an open task; an explicit null deadline clears it.</summary>
    [HttpPatch("{taskId}")]
    public async Task<IActionResult> UpdateTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        var payload = await TaskUpdate.ParseAsync(Request);
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId);
        TaskService.RequireTaskCreator(task, auth.User);
        if (task.Status != TaskStatuses.Open)
        {
            throw StateConflict("Only an open task can be edited");
        }

        if ((payload.HasTitle && payload.Title is null)
            || (payload.HasDescription && payload.Description is null))
        {
            throw new AppError("TASK_FIELD_REQUIRED", "Task fields cannot be null", 422);
        }
        if (payload.HasTitle)
        {
            task.Title = payload.Title!;
        }
        if (payload.HasDescription)
        {
            task.Description = payload.Description!;
        }
        if (payload.HasDeadlineAt)
        {
            task.DeadlineAt = payload.DeadlineAt;
        }
        if (payload.AnyChanges)
        {
            task.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        var updated = await _db.GetTaskAsync(task.Id);
        return this.SnakeJson(TaskService.ToRead(updated));
    }

    /// <summary>Mark an open task completed (creator only).</summary>
    [HttpPost("{taskId}/complete")]
    public async Task<IActionResult> CompleteTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId);
        TaskService.RequireTaskCreator(task, auth.User);
        if (task.Status != TaskStatuses.Open)
        {
            throw StateConflict("Only an open task can be completed");
        }
        var now = DateTime.UtcNow;
        task.Status = TaskStatuses.Completed;
        task.CompletedAt = now;
        task.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        var completed = await _db.GetTaskAsync(task.Id);
        return this.SnakeJson(TaskService.ToRead(completed));
    }

    /// <summary>Close an open task (creator or administrator).</summary>
    [HttpPost("{taskId}/close")]
    public async Task<IActionResult> CloseTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId);
        TaskService.RequireTaskCreatorOrAdmin(task, auth.User);
        if (task.Status != TaskStatuses.Open)
        {
            throw StateConflict("Only an open task can be closed");
        }
        var now = DateTime.UtcNow;
        task.Status = TaskStatuses.Closed;
        task.ClosedAt = now;
        task.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        var closed = await _db.GetTaskAsync(task.Id);
        return this.SnakeJson(TaskService.ToRead(closed));
    }
}
