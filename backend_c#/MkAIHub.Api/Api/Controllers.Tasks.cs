using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>Task CRUD, closure actions, participation, and submissions.</summary>
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
        var participated = QueryParams.ParseBool(Request, errors, "participated");
        var pendingReview = QueryParams.ParseBool(Request, errors, "pending_review");
        var competitionOnly = QueryParams.ParseBool(Request, errors, "competition_only");
        var pendingCompetitionReview = QueryParams.ParseBool(Request, errors, "pending_competition_review");
        var statusFilter = QueryParams.ParseEnum(Request, errors, "status", TaskStatuses.All);
        QueryParams.ThrowIfErrors(errors);

        IQueryable<TaskItem> query = _db.Tasks.WithCreator();
        if (mine)
        {
            query = query.Where(task => task.CreatorId == auth.User.Id);
        }
        if (participated)
        {
            var participatedTaskIds = _db.TaskParticipants
                .Where(participant =>
                    participant.UserId == auth.User.Id
                    && participant.Status == ParticipantStatuses.Active)
                .Select(participant => participant.TaskId);
            query = query.Where(task => participatedTaskIds.Contains(task.Id));
        }
        if (pendingReview)
        {
            query = query.Where(task => task.CreatorId == auth.User.Id
                && _db.TaskSubmissions.Any(submission =>
                    submission.TaskId == task.Id
                    && submission.IsCurrent
                    && TaskClosureService.PendingSubmissionStatuses.Contains(submission.Status)));
        }
        if (competitionOnly)
        {
            query = query.Where(task => task.CompetitionId != null);
        }
        if (pendingCompetitionReview)
        {
            if (auth.User.Role != UserRoles.SystemAdmin)
            {
                throw new AppError("FORBIDDEN", "Only administrators can review competition submissions", 403);
            }
            var unreviewedTaskIds = await _db.TaskSubmissions
                .Where(submission => submission.IsCurrent
                    && !_db.CompetitionReviews.Any(review => review.TaskSubmissionId == submission.Id))
                .Select(submission => submission.TaskId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (unreviewedTaskIds.Count == 0)
            {
                query = query.Where(task => false);
            }
            else
            {
                var publishedCompetitionIds = await _db.Competitions
                    .Where(competition => competition.Status == CompetitionLifecycles.Published)
                    .Select(competition => competition.Id)
                    .ToListAsync(cancellationToken);
                query = query.Where(task => unreviewedTaskIds.Contains(task.Id)
                    && task.CompetitionId != null
                    && publishedCompetitionIds.Contains(task.CompetitionId.Value));
            }
        }
        if (statusFilter is not null)
        {
            query = query.Where(task => task.Status == statusFilter);
        }
        if (auth.User.Role != UserRoles.SystemAdmin)
        {
            // Tasks of DRAFT competitions follow the competition visibility rule;
            // a non-empty draft set also hides standalone rows exactly like the
            // SQL "competition_id NOT IN (...)" NULL semantics of the Python list.
            var draftCompetitionIds = await _db.Competitions
                .Where(competition => competition.Status == CompetitionLifecycles.Draft)
                .Select(competition => competition.Id)
                .ToListAsync(cancellationToken);
            if (draftCompetitionIds.Count > 0)
            {
                query = query.Where(task => task.CompetitionId != null
                    && !draftCompetitionIds.Contains(task.CompetitionId.Value));
            }
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
        return this.SnakeJson(await _db.ToReadAsync(created, auth.User), statusCode: 201);
    }

    [HttpGet("{taskId}")]
    public async Task<IActionResult> GetTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId, auth.User);
        return this.SnakeJson(await _db.ToReadAsync(task, auth.User));
    }

    /// <summary>Update a non-terminal task; an explicit null deadline clears it.</summary>
    [HttpPatch("{taskId}")]
    public async Task<IActionResult> UpdateTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        var payload = await TaskUpdate.ParseAsync(Request);
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId, auth.User);
        TaskService.RequireTaskCreator(task, auth.User);
        if (TaskService.TerminalStatuses.Contains(task.Status))
        {
            throw StateConflict("A finished task cannot be edited");
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
        return this.SnakeJson(await _db.ToReadAsync(updated, auth.User));
    }

    /// <summary>Complete a task (creator only) that has an accepted submission.</summary>
    [HttpPost("{taskId}/complete")]
    public async Task<IActionResult> CompleteTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId, auth.User);
        await _db.CompleteTaskAsync(task, auth.User);
        var completed = await _db.GetTaskAsync(task.Id);
        return this.SnakeJson(await _db.ToReadAsync(completed, auth.User));
    }

    /// <summary>Close a non-terminal task (creator or administrator).</summary>
    [HttpPost("{taskId}/close")]
    public async Task<IActionResult> CloseTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId, auth.User);
        await _db.CloseTaskAsync(task, auth.User);
        var closed = await _db.GetTaskAsync(task.Id);
        return this.SnakeJson(await _db.ToReadAsync(closed, auth.User));
    }

    /// <summary>Join a task as the current user.</summary>
    [HttpPost("{taskId}/participants")]
    public async Task<IActionResult> JoinTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId, auth.User);
        var participant = await _db.JoinTaskAsync(task, auth.User);
        return this.SnakeJson(TaskClosureService.ToParticipantDto(participant), statusCode: 201);
    }

    /// <summary>Leave a task as the current user.</summary>
    [HttpDelete("{taskId}/participants/me")]
    public async Task<IActionResult> LeaveTask(string taskId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId, auth.User);
        var participant = await _db.LeaveTaskAsync(task, auth.User);
        return this.SnakeJson(TaskClosureService.ToParticipantDto(participant));
    }

    /// <summary>List task participants, oldest joined first.</summary>
    [HttpGet("{taskId}/participants")]
    public async Task<IActionResult> ListParticipants(string taskId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        await _db.GetTaskAsync(parsedId, auth.User);
        var participants = await _db.TaskParticipants
            .Include(participant => participant.User)
            .Where(participant => participant.TaskId == parsedId)
            .OrderBy(participant => participant.JoinedAt)
            .ThenBy(participant => participant.Id)
            .ToListAsync(cancellationToken);
        return this.SnakeJson(new TaskParticipantListResponseDto(
            participants.Select(TaskClosureService.ToParticipantDto).ToList()));
    }

    /// <summary>List task submissions filtered by the visibility matrix.</summary>
    [HttpGet("{taskId}/submissions")]
    public async Task<IActionResult> ListTaskSubmissions(string taskId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        var page = QueryParams.ParseInt(Request, errors, "page", 1, minimum: 1);
        var pageSize = QueryParams.ParseInt(Request, errors, "page_size", 20, minimum: 1, maximum: 100);
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId, auth.User);
        IQueryable<TaskSubmission> query = _db.TaskSubmissions;
        query = query.Where(submission => submission.TaskId == parsedId);
        if (!TaskClosureService.CanViewAllSubmissions(task, auth.User))
        {
            // Others see their own rounds plus accepted submissions once the
            // task is completed.
            var ownParticipantIds = _db.TaskParticipants
                .Where(participant => participant.TaskId == task.Id && participant.UserId == auth.User.Id)
                .Select(participant => participant.Id);
            var taskCompleted = task.Status == TaskStatuses.Completed;
            query = query.Where(submission =>
                ownParticipantIds.Contains(submission.ParticipantId)
                || (taskCompleted && submission.Status == SubmissionStatuses.Accepted));
        }
        var total = await query.CountAsync(cancellationToken);
        var submissions = await query
            .WithDetails()
            .OrderByDescending(submission => submission.SubmittedAt)
            .ThenByDescending(submission => submission.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var items = new List<TaskSubmissionDto>();
        foreach (var submission in submissions)
        {
            items.Add(await _db.ToSubmissionDtoAsync(submission, viewer: auth.User));
        }
        return this.SnakeJson(new TaskSubmissionListResponseDto(items, page, pageSize, total));
    }

    /// <summary>Submit one of my published artifacts to the task.</summary>
    [HttpPost("{taskId}/submissions")]
    public async Task<IActionResult> SubmitToTask(string taskId, CancellationToken cancellationToken)
    {
        var payload = await TaskSubmissionCreate.ParseAsync(Request);
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "task_id", "taskId");
        QueryParams.ThrowIfErrors(errors);

        var task = await _db.GetTaskAsync(parsedId, auth.User);
        var submission = await _db.CreateSubmissionAsync(task, auth.User, payload.ArtifactId, payload.Note);
        return this.SnakeJson(await _db.ToSubmissionDtoAsync(submission, viewer: auth.User), statusCode: 201);
    }
}
