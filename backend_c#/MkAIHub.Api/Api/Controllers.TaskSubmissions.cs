using Microsoft.AspNetCore.Mvc;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>Decision actions on task submissions (revision, accept, reject, review).</summary>
[Route("api/v1/task-submissions")]
public sealed class TaskSubmissionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;
    private readonly ILogger _logger;

    public TaskSubmissionsController(AppDbContext db, Authenticator auth, ILoggerFactory loggerFactory)
    {
        _db = db;
        _auth = auth;
        // The audit logger mirrors Python's "mkaihub" application logger.
        _logger = loggerFactory.CreateLogger("mkaihub");
    }

    /// <summary>Audit administrators deciding submissions they did not publish.</summary>
    private void LogAdminDecision(string action, TaskSubmission submission, User user)
    {
        if (user.Role == UserRoles.SystemAdmin && submission.Task!.CreatorId != user.Id)
        {
            _logger.LogAdminAction(
                $"task.submission_{action}",
                user.Id,
                "task_submission",
                submission.Id,
                new Dictionary<string, object?> { ["task_id"] = submission.TaskId });
        }
    }

    [HttpPost("{submissionId}/request-revision")]
    public async Task<IActionResult> RequestRevision(string submissionId, CancellationToken cancellationToken)
    {
        var payload = await SubmissionRevisionRequest.ParseAsync(Request);
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "submission_id", "submissionId");
        QueryParams.ThrowIfErrors(errors);

        var submission = await _db.GetSubmissionAsync(parsedId);
        var updated = await _db.DecideSubmissionAsync(submission, auth.User, "request_revision", payload.Note);
        LogAdminDecision("request_revision", updated, auth.User);
        return this.SnakeJson(await _db.ToSubmissionDtoAsync(updated, viewer: auth.User));
    }

    [HttpPost("{submissionId}/accept")]
    public async Task<IActionResult> AcceptSubmission(string submissionId, CancellationToken cancellationToken)
    {
        var payload = await SubmissionAcceptRequest.ParseAsync(Request);
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "submission_id", "submissionId");
        QueryParams.ThrowIfErrors(errors);

        var submission = await _db.GetSubmissionAsync(parsedId);
        var updated = await _db.DecideSubmissionAsync(
            submission, auth.User, "accept", payload?.Note);
        LogAdminDecision("accept", updated, auth.User);
        return this.SnakeJson(await _db.ToSubmissionDtoAsync(updated, viewer: auth.User));
    }

    [HttpPost("{submissionId}/reject")]
    public async Task<IActionResult> RejectSubmission(string submissionId, CancellationToken cancellationToken)
    {
        var payload = await SubmissionRejectRequest.ParseAsync(Request);
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "submission_id", "submissionId");
        QueryParams.ThrowIfErrors(errors);

        var submission = await _db.GetSubmissionAsync(parsedId);
        var updated = await _db.DecideSubmissionAsync(submission, auth.User, "reject", payload.Note);
        LogAdminDecision("reject", updated, auth.User);
        return this.SnakeJson(await _db.ToSubmissionDtoAsync(updated, viewer: auth.User));
    }

    /// <summary>Upsert the official score of a current competition submission round.</summary>
    [HttpPost("{submissionId}/competition-review")]
    public async Task<IActionResult> ReviewCompetitionSubmission(string submissionId, CancellationToken cancellationToken)
    {
        var payload = await CompetitionReviewRequest.ParseAsync(Request);
        var auth = _auth.RequireAdminCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "submission_id", "submissionId");
        QueryParams.ThrowIfErrors(errors);

        var submission = await _db.GetSubmissionAsync(parsedId);
        var (_, created) = await _db.ReviewCompetitionSubmissionAsync(
            submission, auth.User, payload.RawScore, payload.Comment);
        _logger.LogAdminAction(
            created ? "competition_review.create" : "competition_review.update",
            auth.User.Id,
            "task_submission",
            submission.Id,
            new Dictionary<string, object?>
            {
                ["task_id"] = submission.TaskId,
                ["raw_score"] = TaskClosureService.FormatScore2(payload.RawScore),
            });
        return this.SnakeJson(await _db.ToSubmissionDtoAsync(submission, viewer: auth.User));
    }
}
