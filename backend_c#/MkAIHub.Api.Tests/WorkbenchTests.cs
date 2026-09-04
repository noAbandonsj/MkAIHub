using System.Net;

namespace MkAIHub.Api.Tests;

/// <summary>Port of backend/tests/test_workbench.py.</summary>
public sealed class WorkbenchTests
{
    [Fact]
    public async Task WorkbenchCountsStatisticsAndCrossModuleFilters()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        await environment.SeedUserAsync("creator");
        await environment.SeedUserAsync("member");
        using var bossClient = environment.CreateClient();
        using var creatorClient = environment.CreateClient();
        using var memberClient = environment.CreateClient();
        var boss = await bossClient.LoginAsync("boss");
        var creator = await creatorClient.LoginAsync("creator");
        var member = await memberClient.LoginAsync("member");

        int independentTaskId;
        using (var independent = await creatorClient.PostJsonAsync(
            "/api/v1/tasks",
            new { title = "独立闭环任务", description = "用于待验收统计。", deadline_at = (string?)null },
            creator))
        {
            using var body = await independent.ReadJsonAsync();
            independentTaskId = body.RootElement.GetProperty("id").GetInt32();
        }
        Assert.Equal(
            HttpStatusCode.Created,
            (await memberClient.PostActionAsync(
                $"/api/v1/tasks/{independentTaskId}/participants", member)).StatusCode);
        int independentArtifactId;
        using (var artifact = await memberClient.PublishArtifactAsync(member, new { title = "独立任务成果" }))
        {
            independentArtifactId = artifact.RootElement.GetProperty("id").GetInt32();
        }
        int independentSubmissionId;
        using (var submission = await memberClient.PostJsonAsync(
            $"/api/v1/tasks/{independentTaskId}/submissions",
            new { artifact_id = independentArtifactId },
            member))
        {
            using var body = await submission.ReadJsonAsync();
            independentSubmissionId = body.RootElement.GetProperty("id").GetInt32();
        }

        int competitionId;
        using (var competition = await bossClient.PostJsonAsync(
            "/api/v1/admin/competitions",
            new
            {
                title = "批次十竞赛",
                summary = "验证竞赛闭环。",
                rules_markdown = "# 规则",
                start_at = "2020-01-01T00:00:00Z",
                end_at = "2999-01-01T00:00:00Z",
            },
            boss))
        {
            using var body = await competition.ReadJsonAsync();
            competitionId = body.RootElement.GetProperty("id").GetInt32();
        }
        int competitionTaskId;
        using (var task = await bossClient.PostJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}/tasks",
            new
            {
                title = "竞赛闭环任务",
                description = "提交一个提示词展品。",
                deadline_at = (string?)null,
                required = true,
                sort_order = 1,
                max_score = "30.00",
                weight = "10.00",
            },
            boss))
        {
            using var body = await task.ReadJsonAsync();
            competitionTaskId = body.RootElement.GetProperty("id").GetInt32();
        }
        Assert.Equal(
            HttpStatusCode.OK,
            (await bossClient.PostActionAsync(
                $"/api/v1/admin/competitions/{competitionId}/publish", boss)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            (await memberClient.PostActionAsync(
                $"/api/v1/competitions/{competitionId}/registrations", member)).StatusCode);
        int competitionArtifactId;
        using (var artifact = await memberClient.PublishArtifactAsync(member, new { title = "竞赛任务成果" }))
        {
            competitionArtifactId = artifact.RootElement.GetProperty("id").GetInt32();
        }
        int competitionSubmissionId;
        using (var submission = await memberClient.PostJsonAsync(
            $"/api/v1/tasks/{competitionTaskId}/submissions",
            new { artifact_id = competitionArtifactId },
            member))
        {
            using var body = await submission.ReadJsonAsync();
            competitionSubmissionId = body.RootElement.GetProperty("id").GetInt32();
        }

        using (var memberSummary = await memberClient.GetAsync("/api/v1/workbench"))
        {
            Assert.Equal(HttpStatusCode.OK, memberSummary.StatusCode);
            using var body = await memberSummary.ReadJsonAsync();
            var counts = body.RootElement.GetProperty("counts");
            Assert.Equal(2, counts.GetProperty("participated_tasks").GetInt32());
            Assert.Equal(1, counts.GetProperty("competition_tasks").GetInt32());
            Assert.Equal(0, counts.GetProperty("pending_task_reviews").GetInt32());
            Assert.Equal(0, counts.GetProperty("pending_competition_reviews").GetInt32());
        }

        using (var creatorSummary = await creatorClient.GetAsync("/api/v1/workbench"))
        {
            using var body = await creatorSummary.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("counts").GetProperty("pending_task_reviews").GetInt32());
        }

        using (var bossSummary = await bossClient.GetAsync("/api/v1/workbench"))
        {
            using var body = await bossSummary.ReadJsonAsync();
            Assert.Equal(
                1,
                body.RootElement.GetProperty("counts").GetProperty("pending_competition_reviews").GetInt32());
            var statistics = body.RootElement.GetProperty("statistics");
            Assert.Equal(2, statistics.GetProperty("participations").GetInt32());
            Assert.Equal(2, statistics.GetProperty("submissions").GetInt32());
            Assert.Equal(0, statistics.GetProperty("accepted_submissions").GetInt32());
            Assert.Equal(1, statistics.GetProperty("competition_task_completions").GetInt32());
            Assert.Equal(0, statistics.GetProperty("published_results").GetInt32());
        }

        using (var competitionTasks = await memberClient.GetAsync(
            "/api/v1/tasks?participated=true&competition_only=true"))
        {
            using var body = await competitionTasks.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(
                competitionTaskId,
                body.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32());
        }

        using (var pendingCompetitionReviews = await bossClient.GetAsync(
            "/api/v1/tasks?pending_competition_review=true"))
        {
            Assert.Equal(HttpStatusCode.OK, pendingCompetitionReviews.StatusCode);
            using var body = await pendingCompetitionReviews.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        }
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await memberClient.GetAsync("/api/v1/tasks?pending_competition_review=true")).StatusCode);

        using (var taskArtifacts = await memberClient.GetAsync("/api/v1/artifacts?source=TASK_RESULT"))
        {
            using var body = await taskArtifacts.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(
                independentArtifactId,
                body.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32());
            Assert.Equal(
                new[] { "TASK_RESULT" },
                body.RootElement.GetProperty("items")[0].GetProperty("source_types")
                    .EnumerateArray()
                    .Select(item => item.GetString())
                    .ToArray());
        }

        using (var competitionArtifacts = await memberClient.GetAsync(
            "/api/v1/artifacts?source=COMPETITION_ENTRY"))
        {
            using var body = await competitionArtifacts.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(
                competitionArtifactId,
                body.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32());
            Assert.Equal(
                new[] { "COMPETITION_ENTRY" },
                body.RootElement.GetProperty("items")[0].GetProperty("source_types")
                    .EnumerateArray()
                    .Select(item => item.GetString())
                    .ToArray());
        }

        Assert.Equal(
            HttpStatusCode.OK,
            (await creatorClient.PostJsonAsync(
                $"/api/v1/task-submissions/{independentSubmissionId}/accept",
                new { note = "验收通过" },
                creator)).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await bossClient.PostJsonAsync(
                $"/api/v1/task-submissions/{competitionSubmissionId}/competition-review",
                new { raw_score = "20.00", comment = "评审完成" },
                boss)).StatusCode);

        using (var creatorSummary = await creatorClient.GetAsync("/api/v1/workbench"))
        {
            using var body = await creatorSummary.ReadJsonAsync();
            Assert.Equal(0, body.RootElement.GetProperty("counts").GetProperty("pending_task_reviews").GetInt32());
        }
        using (var refreshedBoss = await bossClient.GetAsync("/api/v1/workbench"))
        {
            using var body = await refreshedBoss.ReadJsonAsync();
            Assert.Equal(
                0,
                body.RootElement.GetProperty("counts").GetProperty("pending_competition_reviews").GetInt32());
            Assert.Equal(
                1,
                body.RootElement.GetProperty("statistics").GetProperty("accepted_submissions").GetInt32());
        }
    }

    [Fact]
    public async Task WorkbenchCompetitionTasksCountsEachTaskNotEachCompetition()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        await environment.SeedUserAsync("member");
        using var bossClient = environment.CreateClient();
        using var memberClient = environment.CreateClient();
        var boss = await bossClient.LoginAsync("boss");
        var member = await memberClient.LoginAsync("member");

        int competitionId;
        using (var competition = await bossClient.PostJsonAsync(
            "/api/v1/admin/competitions",
            new
            {
                title = "多任务竞赛",
                summary = "验证竞赛任务计数按任务数而非竞赛数统计。",
                rules_markdown = "# 规则",
                start_at = "2020-01-01T00:00:00Z",
                end_at = "2999-01-01T00:00:00Z",
            },
            boss))
        {
            using var body = await competition.ReadJsonAsync();
            competitionId = body.RootElement.GetProperty("id").GetInt32();
        }
        for (var sortOrder = 1; sortOrder <= 2; sortOrder += 1)
        {
            using var task = await bossClient.PostJsonAsync(
                $"/api/v1/admin/competitions/{competitionId}/tasks",
                new
                {
                    title = $"竞赛任务{sortOrder}",
                    description = "报名后自动领取。",
                    deadline_at = (string?)null,
                    required = true,
                    sort_order = sortOrder,
                    max_score = "30.00",
                    weight = "10.00",
                },
                boss);
            Assert.Equal(HttpStatusCode.Created, task.StatusCode);
        }
        Assert.Equal(
            HttpStatusCode.OK,
            (await bossClient.PostActionAsync(
                $"/api/v1/admin/competitions/{competitionId}/publish", boss)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Created,
            (await memberClient.PostActionAsync(
                $"/api/v1/competitions/{competitionId}/registrations", member)).StatusCode);

        using var summary = await memberClient.GetAsync("/api/v1/workbench");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        using var summaryBody = await summary.ReadJsonAsync();
        var counts = summaryBody.RootElement.GetProperty("counts");
        Assert.Equal(2, counts.GetProperty("participated_tasks").GetInt32());
        Assert.Equal(2, counts.GetProperty("competition_tasks").GetInt32());
    }
}
