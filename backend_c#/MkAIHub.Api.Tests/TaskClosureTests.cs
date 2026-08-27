using System.Net;
using Microsoft.Data.Sqlite;

namespace MkAIHub.Api.Tests;

/// <summary>Port of backend/tests/test_task_closure.py.</summary>
public sealed class TaskClosureTests
{
    [Fact]
    public async Task TaskClosureFullFlow()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("creator");
        await environment.SeedUserAsync("memberb");
        await environment.SeedUserAsync("memberc");
        await environment.SeedUserAsync("outsider");
        using var creatorClient = environment.CreateClient();
        using var memberbClient = environment.CreateClient();
        using var membercClient = environment.CreateClient();
        using var outsiderClient = environment.CreateClient();
        var creator = await creatorClient.LoginAsync("creator");
        var memberb = await memberbClient.LoginAsync("memberb");
        var memberc = await membercClient.LoginAsync("memberc");
        var outsider = await outsiderClient.LoginAsync("outsider");

        int taskId;
        using (var created = await creatorClient.PostJsonAsync(
            "/api/v1/tasks",
            new { title = "闭环任务", description = "完成参与、提交与验收闭环。", deadline_at = (string?)null },
            creator))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var body = await created.ReadJsonAsync();
            taskId = body.RootElement.GetProperty("id").GetInt32();
            Assert.Equal("OPEN", body.RootElement.GetProperty("status").GetString());
            Assert.True(body.RootElement.GetProperty("my_participation").ValueKind == JsonValueKind.Null);
            Assert.Equal(0, body.RootElement.GetProperty("participant_count").GetInt32());
        }

        // Outsiders cannot submit before participating.
        using (var outsiderDraft = await outsiderClient.CreateDraftAsync(outsider))
        {
            var outsiderArtifactId = outsiderDraft.RootElement.GetProperty("id").GetInt32();
            using var rejected = await outsiderClient.PostJsonAsync(
                $"/api/v1/tasks/{taskId}/submissions",
                new { artifact_id = outsiderArtifactId, note = "越权提交" },
                outsider);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            using var body = await rejected.ReadJsonAsync();
            Assert.Equal("FORBIDDEN", body.RootElement.GetProperty("code").GetString());
        }

        using (var joined = await memberbClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", memberb))
        {
            Assert.Equal(HttpStatusCode.Created, joined.StatusCode);
            using var body = await joined.ReadJsonAsync();
            Assert.Equal("ACTIVE", body.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                "memberb",
                body.RootElement.GetProperty("user").GetProperty("username").GetString());
        }
        using (var duplicated = await memberbClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", memberb))
        {
            Assert.Equal(HttpStatusCode.Conflict, duplicated.StatusCode);
            using var body = await duplicated.ReadJsonAsync();
            Assert.Equal("TASK_ALREADY_PARTICIPATED", body.RootElement.GetProperty("code").GetString());
        }

        using (var detail = await creatorClient.GetAsync($"/api/v1/tasks/{taskId}"))
        {
            using var body = await detail.ReadJsonAsync();
            Assert.Equal("IN_PROGRESS", body.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, body.RootElement.GetProperty("participant_count").GetInt32());
            Assert.True(body.RootElement.GetProperty("my_participation").ValueKind == JsonValueKind.Null);
        }
        using (var bDetail = await memberbClient.GetAsync($"/api/v1/tasks/{taskId}"))
        {
            using var body = await bDetail.ReadJsonAsync();
            Assert.Equal(
                "ACTIVE",
                body.RootElement.GetProperty("my_participation").GetProperty("status").GetString());
        }

        Assert.Equal(
            HttpStatusCode.Created,
            (await membercClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", memberc)).StatusCode);

        // Draft artifacts and other users' artifacts cannot be submitted.
        int bDraftId;
        using (var bDraft = await memberbClient.CreateDraftAsync(memberb, new { title = "未发布成果" }))
        {
            bDraftId = bDraft.RootElement.GetProperty("id").GetInt32();
        }
        using (var draftRejected = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = bDraftId },
            memberb))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, draftRejected.StatusCode);
        }
        int cArtifactId;
        using (var cArtifact = await membercClient.PublishArtifactAsync(memberc, new { title = "成员C的成果" }))
        {
            cArtifactId = cArtifact.RootElement.GetProperty("id").GetInt32();
        }
        using (var foreignRejected = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = cArtifactId },
            memberb))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, foreignRejected.StatusCode);
        }
        using (var unknownArtifact = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = 99999 },
            memberb))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknownArtifact.StatusCode);
        }

        int bArtifactId;
        using (var bArtifact = await memberbClient.PublishArtifactAsync(memberb, new { title = "成员B的成果" }))
        {
            bArtifactId = bArtifact.RootElement.GetProperty("id").GetInt32();
        }
        int submissionId;
        using (var submitted = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = bArtifactId, note = "第一轮提交" },
            memberb))
        {
            Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
            using var body = await submitted.ReadJsonAsync();
            submissionId = body.RootElement.GetProperty("id").GetInt32();
            Assert.Equal("SUBMITTED", body.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, body.RootElement.GetProperty("round_no").GetInt32());
            Assert.True(body.RootElement.GetProperty("is_current").GetBoolean());
            Assert.Equal(bArtifactId, body.RootElement.GetProperty("artifact").GetProperty("id").GetInt32());
            Assert.Equal(
                "memberb",
                body.RootElement.GetProperty("participant").GetProperty("username").GetString());
        }
        using (var reviewing = await creatorClient.GetAsync($"/api/v1/tasks/{taskId}"))
        {
            using var body = await reviewing.ReadJsonAsync();
            Assert.Equal("REVIEWING", body.RootElement.GetProperty("status").GetString());
        }

        using (var pendingAgain = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = bArtifactId },
            memberb))
        {
            Assert.Equal(HttpStatusCode.Conflict, pendingAgain.StatusCode);
            using var body = await pendingAgain.ReadJsonAsync();
            Assert.Equal("SUBMISSION_ALREADY_PENDING", body.RootElement.GetProperty("code").GetString());
        }

        // Only the creator (or an administrator) may decide submissions.
        using (var forbiddenDecide = await membercClient.PostJsonAsync(
            $"/api/v1/task-submissions/{submissionId}/request-revision",
            new { note = "越权退回" },
            memberc))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenDecide.StatusCode);
        }

        using (var revision = await creatorClient.PostJsonAsync(
            $"/api/v1/task-submissions/{submissionId}/request-revision",
            new { note = "请补充适用范围说明" },
            creator))
        {
            Assert.Equal(HttpStatusCode.OK, revision.StatusCode);
            using var body = await revision.ReadJsonAsync();
            Assert.Equal("REVISION_REQUIRED", body.RootElement.GetProperty("status").GetString());
            Assert.NotNull(body.RootElement.GetProperty("revision_requested_at").GetString());
            Assert.Equal(
                "请补充适用范围说明",
                body.RootElement.GetProperty("decision_note").GetString());
        }

        // After a revision request the participant can submit a new round.
        int bArtifactV2Id;
        using (var v2 = await memberbClient.PublishArtifactAsync(memberb, new { title = "成员B的成果（修订）" }))
        {
            bArtifactV2Id = v2.RootElement.GetProperty("id").GetInt32();
        }
        int roundTwoId;
        using (var resubmitted = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = bArtifactV2Id, note = "第二轮提交" },
            memberb))
        {
            Assert.Equal(HttpStatusCode.Created, resubmitted.StatusCode);
            using var body = await resubmitted.ReadJsonAsync();
            roundTwoId = body.RootElement.GetProperty("id").GetInt32();
            Assert.Equal(2, body.RootElement.GetProperty("round_no").GetInt32());
            Assert.True(body.RootElement.GetProperty("is_current").GetBoolean());
        }
        using (var history = await memberbClient.GetAsync($"/api/v1/tasks/{taskId}/submissions"))
        {
            using var body = await history.ReadJsonAsync();
            var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(2, items.Count);
            Assert.True(items[0].GetProperty("is_current").GetBoolean());
            Assert.False(items[1].GetProperty("is_current").GetBoolean());
        }

        int cSubmissionId;
        using (var cSubmitted = await membercClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = cArtifactId, note = "C 的提交" },
            memberc))
        {
            Assert.Equal(HttpStatusCode.Created, cSubmitted.StatusCode);
            using var body = await cSubmitted.ReadJsonAsync();
            cSubmissionId = body.RootElement.GetProperty("id").GetInt32();
        }

        // Deciding a historical (non-current) round is rejected.
        using (var staleRound = await creatorClient.PostJsonAsync(
            $"/api/v1/task-submissions/{submissionId}/accept",
            new { note = "验收旧轮次" },
            creator))
        {
            Assert.Equal(HttpStatusCode.Conflict, staleRound.StatusCode);
        }

        using (var missingNote = await creatorClient.PostJsonAsync(
            $"/api/v1/task-submissions/{cSubmissionId}/reject",
            new { },
            creator))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, missingNote.StatusCode);
        }

        using (var rejectedC = await creatorClient.PostJsonAsync(
            $"/api/v1/task-submissions/{cSubmissionId}/reject",
            new { note = "本次不采用" },
            creator))
        {
            Assert.Equal(HttpStatusCode.OK, rejectedC.StatusCode);
            using var body = await rejectedC.ReadJsonAsync();
            Assert.Equal("REJECTED", body.RootElement.GetProperty("status").GetString());
            Assert.NotNull(body.RootElement.GetProperty("decided_at").GetString());
        }

        using (var accepted = await creatorClient.PostJsonAsync(
            $"/api/v1/task-submissions/{roundTwoId}/accept",
            new { note = "验收通过" },
            creator))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            using var body = await accepted.ReadJsonAsync();
            Assert.Equal("ACCEPTED", body.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                "creator",
                body.RootElement.GetProperty("decider").GetProperty("username").GetString());
        }

        // All rounds decided: the derived task status falls back to IN_PROGRESS.
        using (var inProgress = await creatorClient.GetAsync($"/api/v1/tasks/{taskId}"))
        {
            using var body = await inProgress.ReadJsonAsync();
            Assert.Equal("IN_PROGRESS", body.RootElement.GetProperty("status").GetString());
        }

        using (var completed = await creatorClient.PostActionAsync($"/api/v1/tasks/{taskId}/complete", creator))
        {
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            using var body = await completed.ReadJsonAsync();
            Assert.Equal("COMPLETED", body.RootElement.GetProperty("status").GetString());
            Assert.NotNull(body.RootElement.GetProperty("completed_at").GetString());
        }

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await creatorClient.PostActionAsync($"/api/v1/tasks/{taskId}/complete", creator)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await outsiderClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", outsider)).StatusCode);
        using (var terminalSubmit = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = bArtifactId },
            memberb))
        {
            Assert.Equal(HttpStatusCode.Conflict, terminalSubmit.StatusCode);
        }
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await memberbClient.DeleteAsync(memberb, $"/api/v1/tasks/{taskId}/participants/me")).StatusCode);
    }

    [Fact]
    public async Task SubmissionVisibilityAndFilters()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("creator");
        await environment.SeedUserAsync("memberb");
        await environment.SeedUserAsync("outsider");
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        using var creatorClient = environment.CreateClient();
        using var memberbClient = environment.CreateClient();
        using var outsiderClient = environment.CreateClient();
        using var bossClient = environment.CreateClient();
        var creator = await creatorClient.LoginAsync("creator");
        var memberb = await memberbClient.LoginAsync("memberb");
        var outsider = await outsiderClient.LoginAsync("outsider");
        var boss = await bossClient.LoginAsync("boss");

        int taskId;
        using (var created = await creatorClient.PostJsonAsync(
            "/api/v1/tasks",
            new { title = "可见性任务", description = "验证提交可见性与筛选。", deadline_at = (string?)null },
            creator))
        {
            using var body = await created.ReadJsonAsync();
            taskId = body.RootElement.GetProperty("id").GetInt32();
        }
        await memberbClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", memberb);
        int artifactId;
        using (var artifact = await memberbClient.PublishArtifactAsync(memberb, new { title = "B 的可见性成果" }))
        {
            artifactId = artifact.RootElement.GetProperty("id").GetInt32();
        }
        int submissionId;
        using (var submitted = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = artifactId },
            memberb))
        {
            using var body = await submitted.ReadJsonAsync();
            submissionId = body.RootElement.GetProperty("id").GetInt32();
        }

        // Before completion only the participant, creator, and admin see rounds.
        using (var anonymousClient = environment.CreateClient())
        {
            using var anonymous = await anonymousClient.GetAsync($"/api/v1/tasks/{taskId}/submissions");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
        Assert.Equal(0, await TotalAsync(outsiderClient, $"/api/v1/tasks/{taskId}/submissions"));
        Assert.Equal(1, await TotalAsync(memberbClient, $"/api/v1/tasks/{taskId}/submissions"));
        Assert.Equal(1, await TotalAsync(creatorClient, $"/api/v1/tasks/{taskId}/submissions"));
        Assert.Equal(1, await TotalAsync(bossClient, $"/api/v1/tasks/{taskId}/submissions"));

        // Filters.
        Assert.Equal(0, await TotalAsync(creatorClient, "/api/v1/tasks?participated=true"));
        Assert.Equal(1, await TotalAsync(memberbClient, "/api/v1/tasks?participated=true"));
        Assert.Equal(1, await TotalAsync(creatorClient, "/api/v1/tasks?pending_review=true"));
        Assert.Equal(0, await TotalAsync(memberbClient, "/api/v1/tasks?pending_review=true"));

        using (var accepted = await creatorClient.PostJsonAsync(
            $"/api/v1/task-submissions/{submissionId}/accept",
            new { note = "通过" },
            creator))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        Assert.Equal(
            HttpStatusCode.OK,
            (await creatorClient.PostActionAsync($"/api/v1/tasks/{taskId}/complete", creator)).StatusCode);

        // After completion everyone sees the accepted final result.
        using (var outsiderItems = await outsiderClient.GetAsync($"/api/v1/tasks/{taskId}/submissions"))
        {
            using var body = await outsiderItems.ReadJsonAsync();
            var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.Single(items);
            Assert.Equal("ACCEPTED", items[0].GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task ParticipantLeaveAndRejoin()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("creator");
        await environment.SeedUserAsync("memberb");
        using var creatorClient = environment.CreateClient();
        using var memberbClient = environment.CreateClient();
        var creator = await creatorClient.LoginAsync("creator");
        var memberb = await memberbClient.LoginAsync("memberb");

        int taskId;
        using (var created = await creatorClient.PostJsonAsync(
            "/api/v1/tasks",
            new { title = "退出任务", description = "验证退出与重新参与。", deadline_at = (string?)null },
            creator))
        {
            using var body = await created.ReadJsonAsync();
            taskId = body.RootElement.GetProperty("id").GetInt32();
        }

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await memberbClient.DeleteAsync(memberb, $"/api/v1/tasks/{taskId}/participants/me")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            (await memberbClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", memberb)).StatusCode);
        using (var inProgress = await creatorClient.GetAsync($"/api/v1/tasks/{taskId}"))
        {
            using var body = await inProgress.ReadJsonAsync();
            Assert.Equal("IN_PROGRESS", body.RootElement.GetProperty("status").GetString());
        }

        using (var left = await memberbClient.DeleteAsync(memberb, $"/api/v1/tasks/{taskId}/participants/me"))
        {
            Assert.Equal(HttpStatusCode.OK, left.StatusCode);
            using var body = await left.ReadJsonAsync();
            Assert.Equal("LEFT", body.RootElement.GetProperty("status").GetString());
            Assert.NotNull(body.RootElement.GetProperty("left_at").GetString());
        }
        using (var openAgain = await creatorClient.GetAsync($"/api/v1/tasks/{taskId}"))
        {
            using var body = await openAgain.ReadJsonAsync();
            Assert.Equal("OPEN", body.RootElement.GetProperty("status").GetString());
        }

        using (var rejoined = await memberbClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", memberb))
        {
            Assert.Equal(HttpStatusCode.Created, rejoined.StatusCode);
            using var body = await rejoined.ReadJsonAsync();
            Assert.Equal("ACTIVE", body.RootElement.GetProperty("status").GetString());
        }

        int artifactId;
        using (var artifact = await memberbClient.PublishArtifactAsync(memberb, new { title = "退出前成果" }))
        {
            artifactId = artifact.RootElement.GetProperty("id").GetInt32();
        }
        using (var submitted = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = artifactId },
            memberb))
        {
            Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        }
        using (var blocked = await memberbClient.DeleteAsync(memberb, $"/api/v1/tasks/{taskId}/participants/me"))
        {
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            using var body = await blocked.ReadJsonAsync();
            Assert.Equal("TASK_SUBMISSION_EXISTS", body.RootElement.GetProperty("code").GetString());
        }

        using (var participants = await creatorClient.GetAsync($"/api/v1/tasks/{taskId}/participants"))
        {
            using var body = await participants.ReadJsonAsync();
            Assert.Equal(
                new[] { "memberb" },
                body.RootElement.GetProperty("items")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("user").GetProperty("username").GetString())
                    .ToArray());
        }
    }

    [Fact]
    public async Task TaskCloseReopenAndAdminDecisions()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("creator");
        await environment.SeedUserAsync("memberb");
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        using var creatorClient = environment.CreateClient();
        using var memberbClient = environment.CreateClient();
        using var bossClient = environment.CreateClient();
        var creator = await creatorClient.LoginAsync("creator");
        var memberb = await memberbClient.LoginAsync("memberb");
        var boss = await bossClient.LoginAsync("boss");

        int taskId;
        using (var created = await creatorClient.PostJsonAsync(
            "/api/v1/tasks",
            new { title = "重开任务", description = "验证关闭与重开。", deadline_at = (string?)null },
            creator))
        {
            using var body = await created.ReadJsonAsync();
            taskId = body.RootElement.GetProperty("id").GetInt32();
        }
        await memberbClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", memberb);

        using (var closed = await creatorClient.PostActionAsync($"/api/v1/tasks/{taskId}/close", creator))
        {
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            using var body = await closed.ReadJsonAsync();
            Assert.Equal("CLOSED", body.RootElement.GetProperty("status").GetString());
        }

        // Only administrators can reopen a closed task.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await creatorClient.PostActionAsync($"/api/v1/admin/tasks/{taskId}/reopen", creator)).StatusCode);
        using (var reopened = await bossClient.PostActionAsync($"/api/v1/admin/tasks/{taskId}/reopen", boss))
        {
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
            using var body = await reopened.ReadJsonAsync();
            Assert.Equal("IN_PROGRESS", body.RootElement.GetProperty("status").GetString());
            Assert.True(body.RootElement.GetProperty("closed_at").ValueKind == JsonValueKind.Null);
        }

        // A task without participants reopens to OPEN.
        int emptyId;
        using (var empty = await creatorClient.PostJsonAsync(
            "/api/v1/tasks",
            new { title = "空任务", description = "没有参与人。", deadline_at = (string?)null },
            creator))
        {
            using var body = await empty.ReadJsonAsync();
            emptyId = body.RootElement.GetProperty("id").GetInt32();
        }
        Assert.Equal(
            HttpStatusCode.OK,
            (await bossClient.PostActionAsync($"/api/v1/tasks/{emptyId}/close", boss)).StatusCode);
        using (var reopenedEmpty = await bossClient.PostActionAsync($"/api/v1/admin/tasks/{emptyId}/reopen", boss))
        {
            using var body = await reopenedEmpty.ReadJsonAsync();
            Assert.Equal("OPEN", body.RootElement.GetProperty("status").GetString());
        }
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await bossClient.PostActionAsync($"/api/v1/admin/tasks/{emptyId}/reopen", boss)).StatusCode);

        // Administrators may decide submissions as exception handlers.
        int artifactId;
        using (var artifact = await memberbClient.PublishArtifactAsync(memberb, new { title = "管理员验收成果" }))
        {
            artifactId = artifact.RootElement.GetProperty("id").GetInt32();
        }
        int submissionId;
        using (var submitted = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = artifactId },
            memberb))
        {
            using var body = await submitted.ReadJsonAsync();
            submissionId = body.RootElement.GetProperty("id").GetInt32();
        }
        using (var adminAccepted = await bossClient.PostJsonAsync(
            $"/api/v1/task-submissions/{submissionId}/accept",
            new { note = "管理员代为验收" },
            boss))
        {
            Assert.Equal(HttpStatusCode.OK, adminAccepted.StatusCode);
        }
        Assert.Equal(
            HttpStatusCode.OK,
            (await creatorClient.PostActionAsync($"/api/v1/tasks/{taskId}/complete", creator)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (await bossClient.PostActionAsync($"/api/v1/admin/tasks/{taskId}/reopen", boss)).StatusCode);

        using (var unknown = await bossClient.PostJsonAsync(
            "/api/v1/task-submissions/99999/accept",
            new { note = "不存在" },
            boss))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            using var body = await unknown.ReadJsonAsync();
            Assert.Equal("SUBMISSION_NOT_FOUND", body.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task ArtifactTaskSources()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("creator");
        await environment.SeedUserAsync("memberb");
        await environment.SeedUserAsync("memberc");
        await environment.SeedUserAsync("outsider");
        using var creatorClient = environment.CreateClient();
        using var memberbClient = environment.CreateClient();
        using var membercClient = environment.CreateClient();
        using var outsiderClient = environment.CreateClient();
        var creator = await creatorClient.LoginAsync("creator");
        var memberb = await memberbClient.LoginAsync("memberb");
        var memberc = await membercClient.LoginAsync("memberc");
        var outsider = await outsiderClient.LoginAsync("outsider");

        int taskId;
        using (var created = await creatorClient.PostJsonAsync(
            "/api/v1/tasks",
            new { title = "来源任务", description = "验证展品反向来源。", deadline_at = (string?)null },
            creator))
        {
            using var body = await created.ReadJsonAsync();
            taskId = body.RootElement.GetProperty("id").GetInt32();
        }
        await memberbClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", memberb);
        await membercClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", memberc);
        int bArtifactId;
        using (var bArtifact = await memberbClient.PublishArtifactAsync(memberb, new { title = "B 的来源成果" }))
        {
            bArtifactId = bArtifact.RootElement.GetProperty("id").GetInt32();
        }
        int cArtifactId;
        using (var cArtifact = await membercClient.PublishArtifactAsync(memberc, new { title = "C 的待审成果" }))
        {
            cArtifactId = cArtifact.RootElement.GetProperty("id").GetInt32();
        }
        int bSubmissionId;
        using (var bSubmitted = await memberbClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = bArtifactId },
            memberb))
        {
            using var body = await bSubmitted.ReadJsonAsync();
            bSubmissionId = body.RootElement.GetProperty("id").GetInt32();
        }
        await membercClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = cArtifactId },
            memberc);

        // Pending rounds stay private; the submitter always sees their own rounds.
        Assert.Empty(await SourceItemsAsync(outsiderClient, outsider, cArtifactId));
        using (var own = await membercClient.GetAsync($"/api/v1/artifacts/{cArtifactId}/task-submissions"))
        {
            using var body = await own.ReadJsonAsync();
            var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.Single(items);
            Assert.Equal(taskId, items[0].GetProperty("task").GetProperty("id").GetInt32());
            Assert.Equal("REVIEWING", items[0].GetProperty("task").GetProperty("status").GetString());
        }

        using (var accepted = await creatorClient.PostJsonAsync(
            $"/api/v1/task-submissions/{bSubmissionId}/accept",
            new { note = "采用" },
            creator))
        {
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        using (var acceptedView = await outsiderClient.GetAsync($"/api/v1/artifacts/{bArtifactId}/task-submissions"))
        {
            using var body = await acceptedView.ReadJsonAsync();
            var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.Single(items);
            Assert.Equal("ACCEPTED", items[0].GetProperty("status").GetString());
            Assert.Equal(
                "memberb",
                items[0].GetProperty("participant").GetProperty("username").GetString());
        }

        // The still-pending round on the other artifact stays invisible.
        Assert.Empty(await SourceItemsAsync(outsiderClient, outsider, cArtifactId));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await outsiderClient.GetAsync("/api/v1/artifacts/99999/task-submissions")).StatusCode);
    }

    [Fact]
    public async Task ClosureUniqueAndForeignKeyConstraints()
    {
        using var environment = new TestEnvironment();
        using var connection = environment.OpenConnection();
        Execute(connection, "INSERT INTO users (username, display_name, password_hash, role, is_active, created_at, updated_at) "
            + "VALUES ('constraint', 'Constraint', 'x', 'EMPLOYEE', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Execute(connection, "INSERT INTO tasks (title, description, creator_id, status, created_at, updated_at) "
            + "VALUES ('约束任务', '验证唯一与外键约束。', 1, 'IN_PROGRESS', '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Execute(connection, "INSERT INTO artifacts (title, summary, content_markdown, author_id, status, created_at, updated_at) "
            + "VALUES ('约束展品', '用于外键约束的展品。', '# 内容', 1, 'PUBLISHED', '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Execute(connection, "INSERT INTO task_participants (task_id, user_id, status, joined_at, created_at, updated_at) "
            + "VALUES (1, 1, 'ACTIVE', '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')");

        Assert.Throws<SqliteException>(() =>
            Execute(connection, "INSERT INTO task_participants (task_id, user_id, status, joined_at, created_at, updated_at) "
                + "VALUES (1, 1, 'ACTIVE', '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')"));

        Execute(connection, "INSERT INTO task_submissions (task_id, participant_id, artifact_id, round_no, status, is_current, submitted_at, created_at, updated_at) "
            + "VALUES (1, 1, 1, 1, 'SUBMITTED', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Assert.Throws<SqliteException>(() =>
            Execute(connection, "INSERT INTO task_submissions (task_id, participant_id, artifact_id, round_no, status, is_current, submitted_at, created_at, updated_at) "
                + "VALUES (1, 1, 1, 1, 'SUBMITTED', 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')"));

        // RESTRICT keeps referenced tasks and submitted artifacts undeletable.
        Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM tasks WHERE id = 1"));
        Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM artifacts WHERE id = 1"));
    }

    private static async Task<int> TotalAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        using var body = await response.ReadJsonAsync();
        return body.RootElement.GetProperty("total").GetInt32();
    }

    private static async Task<List<JsonElement>> SourceItemsAsync(
        HttpClient client, Dictionary<string, string> headers, int artifactId)
    {
        using var response = await client.GetAsync($"/api/v1/artifacts/{artifactId}/task-submissions");
        using var body = await response.ReadJsonAsync();
        return body.RootElement.GetProperty("items").EnumerateArray().ToList();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
