using System.Net;
using Microsoft.Data.Sqlite;

namespace MkAIHub.Api.Tests;

/// <summary>Port of backend/tests/test_competition_closure.py.</summary>
public sealed class CompetitionClosureTests
{
    private static async Task<JsonDocument> CreateCompetitionAsync(
        HttpClient client,
        Dictionary<string, string> headers,
        object? overrides = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "提示词马拉松",
            ["summary"] = "验证竞赛闭环。",
            ["rules_markdown"] = "# 规则",
            ["start_at"] = "2020-01-01T00:00:00Z",
            ["end_at"] = "2999-01-01T00:00:00Z",
        };
        if (overrides is Dictionary<string, object?> additional)
        {
            foreach (var (key, value) in additional)
            {
                payload[key] = value;
            }
        }
        using var response = await client.PostJsonAsync("/api/v1/admin/competitions", payload, headers);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"create competition failed: {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text);
    }

    private static async Task<JsonDocument> AddTaskAsync(
        HttpClient client,
        Dictionary<string, string> headers,
        int competitionId,
        object? overrides = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "必做任务",
            ["description"] = "提交一个提示词展品。",
            ["deadline_at"] = null,
            ["required"] = true,
            ["sort_order"] = 1,
            ["max_score"] = "30.00",
            ["weight"] = "10.00",
        };
        if (overrides is Dictionary<string, object?> additional)
        {
            foreach (var (key, value) in additional)
            {
                payload[key] = value;
            }
        }
        using var response = await client.PostJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}/tasks", payload, headers);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"add competition task failed: {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text);
    }

    private static async Task<int> RegisterAsync(HttpClient client, Dictionary<string, string> headers, int competitionId)
    {
        using var response = await client.PostActionAsync(
            $"/api/v1/competitions/{competitionId}/registrations", headers);
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"register failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        using var body = await response.ReadJsonAsync();
        return body.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<JsonDocument> JoinAndSubmitAsync(
        HttpClient client,
        Dictionary<string, string> headers,
        int taskId,
        int artifactId,
        string? note = null)
    {
        using var joined = await client.PostActionAsync($"/api/v1/tasks/{taskId}/participants", headers);
        Assert.True(
            joined.StatusCode == HttpStatusCode.Created,
            $"join failed: {(int)joined.StatusCode} {await joined.Content.ReadAsStringAsync()}");
        var payload = new Dictionary<string, object?> { ["artifact_id"] = artifactId };
        if (note is not null)
        {
            payload["note"] = note;
        }
        using var submitted = await client.PostJsonAsync($"/api/v1/tasks/{taskId}/submissions", payload, headers);
        Assert.True(
            submitted.StatusCode == HttpStatusCode.Created,
            $"submit failed: {(int)submitted.StatusCode} {await submitted.Content.ReadAsStringAsync()}");
        return await submitted.ReadJsonAsync();
    }

    private static async Task<HttpResponseMessage> ReviewAsync(
        HttpClient client,
        Dictionary<string, string> headers,
        int submissionId,
        string rawScore,
        string? comment = null)
    {
        var payload = new Dictionary<string, object?> { ["raw_score"] = rawScore };
        if (comment is not null)
        {
            payload["comment"] = comment;
        }
        return await client.PostJsonAsync(
            $"/api/v1/task-submissions/{submissionId}/competition-review", payload, headers);
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        using var body = await response.ReadJsonAsync();
        return body.RootElement.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task CompetitionClosureFullFlow()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        await environment.SeedUserAsync("staffb");
        await environment.SeedUserAsync("staffc");
        using var bossClient = environment.CreateClient();
        using var bClient = environment.CreateClient();
        using var cClient = environment.CreateClient();
        var boss = await bossClient.LoginAsync("boss");
        var b = await bClient.LoginAsync("staffb");
        var c = await cClient.LoginAsync("staffc");

        int competitionId;
        using (var competition = await CreateCompetitionAsync(bossClient, boss))
        {
            competitionId = competition.RootElement.GetProperty("id").GetInt32();
        }
        int requiredTaskId;
        using (var requiredTask = await AddTaskAsync(bossClient, boss, competitionId))
        {
            requiredTaskId = requiredTask.RootElement.GetProperty("id").GetInt32();
            Assert.Equal(competitionId, requiredTask.RootElement.GetProperty("competition_id").GetInt32());
        }
        await AddTaskAsync(bossClient, boss, competitionId, new Dictionary<string, object?>
        {
            ["title"] = "选做任务",
            ["required"] = false,
            ["sort_order"] = 2,
            ["max_score"] = "50.00",
            ["weight"] = "5.00",
        });

        // Registration needs a published competition: DRAFT stays invisible
        // to employees (404) and rejects even the administrator's registration.
        Assert.Equal(
            "COMPETITION_NOT_FOUND",
            await CodeAsync(await bClient.PostActionAsync(
                $"/api/v1/competitions/{competitionId}/registrations", b)));
        Assert.Equal(
            "COMPETITION_STATE_CONFLICT",
            await CodeAsync(await bossClient.PostActionAsync(
                $"/api/v1/competitions/{competitionId}/registrations", boss)));

        using (var published = await bossClient.PostActionAsync(
            $"/api/v1/admin/competitions/{competitionId}/publish", boss))
        {
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
            using var body = await published.ReadJsonAsync();
            Assert.Equal("PUBLISHED", body.RootElement.GetProperty("lifecycle_status").GetString());
        }

        await RegisterAsync(bClient, b, competitionId);
        Assert.Equal(
            "COMPETITION_ALREADY_REGISTERED",
            await CodeAsync(await bClient.PostActionAsync(
                $"/api/v1/competitions/{competitionId}/registrations", b)));

        // Unregistered employees cannot submit competition tasks.
        Assert.Equal(
            HttpStatusCode.Created,
            (await cClient.PostActionAsync($"/api/v1/tasks/{requiredTaskId}/participants", c)).StatusCode);
        int cArtifactId;
        using (var cArtifact = await cClient.PublishArtifactAsync(c, new { title = "C 的未报名成果" }))
        {
            cArtifactId = cArtifact.RootElement.GetProperty("id").GetInt32();
        }
        using (var blocked = await cClient.PostJsonAsync(
            $"/api/v1/tasks/{requiredTaskId}/submissions",
            new { artifact_id = cArtifactId },
            c))
        {
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
            Assert.Equal("COMPETITION_REGISTRATION_REQUIRED", await CodeAsync(blocked));
        }

        // Registered participant submits, resubmits; only the last round counts.
        int bArtifactId;
        using (var bArtifact = await bClient.PublishArtifactAsync(b, new { title = "B 的竞赛成果" }))
        {
            bArtifactId = bArtifact.RootElement.GetProperty("id").GetInt32();
        }
        int firstSubmissionId;
        using (var first = await JoinAndSubmitAsync(bClient, b, requiredTaskId, bArtifactId, "第一轮"))
        {
            firstSubmissionId = first.RootElement.GetProperty("id").GetInt32();
            Assert.Equal(1, first.RootElement.GetProperty("round_no").GetInt32());
            Assert.Equal("SUBMITTED", first.RootElement.GetProperty("status").GetString());
        }
        // Competition tasks stay OPEN no matter how many rounds arrive.
        using (var taskDetail = await bClient.GetAsync($"/api/v1/tasks/{requiredTaskId}"))
        {
            using var body = await taskDetail.ReadJsonAsync();
            Assert.Equal("OPEN", body.RootElement.GetProperty("status").GetString());
        }

        int bArtifactV2Id;
        using (var v2 = await bClient.PublishArtifactAsync(b, new { title = "B 的竞赛成果（二轮）" }))
        {
            bArtifactV2Id = v2.RootElement.GetProperty("id").GetInt32();
        }
        int secondSubmissionId;
        using (var second = await bClient.PostJsonAsync(
            $"/api/v1/tasks/{requiredTaskId}/submissions",
            new { artifact_id = bArtifactV2Id, note = "第二轮" },
            b))
        {
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            using var body = await second.ReadJsonAsync();
            secondSubmissionId = body.RootElement.GetProperty("id").GetInt32();
            Assert.Equal(2, body.RootElement.GetProperty("round_no").GetInt32());
            Assert.True(body.RootElement.GetProperty("is_current").GetBoolean());
        }
        using (var rounds = await bClient.GetAsync($"/api/v1/tasks/{requiredTaskId}/submissions"))
        {
            using var body = await rounds.ReadJsonAsync();
            var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(2, items.Count);
            Assert.False(items[1].GetProperty("is_current").GetBoolean());
        }

        // Accept-style decisions do not apply to competition submissions.
        Assert.Equal(
            "SUBMISSION_STATE_CONFLICT",
            await CodeAsync(await bossClient.PostJsonAsync(
                $"/api/v1/task-submissions/{secondSubmissionId}/accept",
                new { note = "无效" },
                boss)));
        Assert.Equal(
            "TASK_STATE_CONFLICT",
            await CodeAsync(await bossClient.PostActionAsync($"/api/v1/tasks/{requiredTaskId}/complete", boss)));

        // Only administrators review, within the score range, on current rounds.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await ReviewAsync(bClient, b, secondSubmissionId, "10.00")).StatusCode);
        Assert.Equal(
            "REVIEW_SCORE_INVALID",
            await CodeAsync(await ReviewAsync(bossClient, boss, secondSubmissionId, "30.01")));
        Assert.Equal(
            "SUBMISSION_STATE_CONFLICT",
            await CodeAsync(await ReviewAsync(bossClient, boss, firstSubmissionId, "10.00")));

        using (var reviewed = await ReviewAsync(bossClient, boss, secondSubmissionId, "20.00", "结构清晰"))
        {
            Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
            using var body = await reviewed.ReadJsonAsync();
            var review = body.RootElement.GetProperty("competition_review");
            Assert.Equal("20.00", review.GetProperty("raw_score").GetString());
            Assert.Equal(
                "boss",
                review.GetProperty("reviewer").GetProperty("username").GetString());
        }

        // Reviews stay invisible to participants before results are published.
        using (var myView = await bClient.GetAsync($"/api/v1/tasks/{requiredTaskId}/submissions"))
        {
            using var body = await myView.ReadJsonAsync();
            Assert.All(
                body.RootElement.GetProperty("items").EnumerateArray(),
                item => Assert.True(item.GetProperty("competition_review").ValueKind == JsonValueKind.Null));
        }

        // Results cannot be published before the competition ends.
        Assert.Equal(
            "COMPETITION_NOT_ENDED",
            await CodeAsync(await bossClient.PostActionAsync(
                $"/api/v1/admin/competitions/{competitionId}/publish-results", boss)));
        // Shrink end_at into the past to close the window and finish.
        using (var ended = await bossClient.PatchJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}",
            new { end_at = "2026-01-01T00:00:00Z" },
            boss))
        {
            Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        }
        using (var late = await bClient.PostJsonAsync(
            $"/api/v1/tasks/{requiredTaskId}/submissions",
            new { artifact_id = bArtifactId, note = "迟交" },
            b))
        {
            Assert.Equal("COMPETITION_SUBMISSION_CLOSED", await CodeAsync(late));
        }
        Assert.Equal(
            "COMPETITION_REGISTRATION_CLOSED",
            await CodeAsync(await cClient.PostActionAsync(
                $"/api/v1/competitions/{competitionId}/registrations", c)));

        // The optional task's current submission of the qualified participant
        // must be reviewed too; here it has no submission at all (0 points).
        using (var results = await bossClient.PostActionAsync(
            $"/api/v1/admin/competitions/{competitionId}/publish-results", boss))
        {
            Assert.Equal(HttpStatusCode.OK, results.StatusCode);
            using var body = await results.ReadJsonAsync();
            Assert.Equal("RESULT_PUBLISHED", body.RootElement.GetProperty("lifecycle_status").GetString());
        }

        using (var leaderboard = await bClient.GetAsync($"/api/v1/competitions/{competitionId}/results"))
        {
            Assert.Equal(HttpStatusCode.OK, leaderboard.StatusCode);
            using var body = await leaderboard.ReadJsonAsync();
            var rows = body.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.Single(rows);
            Assert.Equal(
                "staffb",
                rows[0].GetProperty("user").GetProperty("username").GetString());
            // 20/30*10 = 6.6667 weighted, optional task missing scores 0.
            Assert.Equal("6.6667", rows[0].GetProperty("total_score").GetString());
            Assert.Equal(1, rows[0].GetProperty("rank").GetInt32());
        }

        // Registration after result publication is a state conflict.
        Assert.Equal(
            "COMPETITION_STATE_CONFLICT",
            await CodeAsync(await cClient.PostActionAsync(
                $"/api/v1/competitions/{competitionId}/registrations", c)));

        // Reviews become visible to participants after publication.
        using (var myView = await bClient.GetAsync($"/api/v1/tasks/{requiredTaskId}/submissions"))
        {
            using var body = await myView.ReadJsonAsync();
            var currentRound = body.RootElement.GetProperty("items").EnumerateArray()
                .Single(item => item.GetProperty("is_current").GetBoolean());
            Assert.Equal(
                "20.00",
                currentRound.GetProperty("competition_review").GetProperty("raw_score").GetString());
            Assert.Equal(1, currentRound.GetProperty("competition_rank").GetInt32());
        }

        // The artifact side shows the competition source after publication.
        using (var sources = await bClient.GetAsync($"/api/v1/artifacts/{bArtifactV2Id}/task-submissions"))
        {
            using var body = await sources.ReadJsonAsync();
            var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
            Assert.Equal(
                competitionId,
                items[0].GetProperty("task").GetProperty("competition").GetProperty("id").GetInt32());
            Assert.Equal(1, items[0].GetProperty("competition_rank").GetInt32());
        }

        // Archived competitions keep their published leaderboard readable.
        using (var archived = await bossClient.PostActionAsync(
            $"/api/v1/admin/competitions/{competitionId}/archive", boss))
        {
            Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
            using var body = await archived.ReadJsonAsync();
            Assert.Equal("ARCHIVED", body.RootElement.GetProperty("lifecycle_status").GetString());
        }
        using (var archivedResults = await bClient.GetAsync($"/api/v1/competitions/{competitionId}/results"))
        {
            Assert.Equal(HttpStatusCode.OK, archivedResults.StatusCode);
        }
        // Archived competitions no longer accept new reviews or edits.
        Assert.Equal(
            "COMPETITION_STATE_CONFLICT",
            await CodeAsync(await ReviewAsync(bossClient, boss, secondSubmissionId, "21.00")));
        Assert.Equal(
            "COMPETITION_STATE_CONFLICT",
            await CodeAsync(await bossClient.PatchJsonAsync(
                $"/api/v1/admin/competitions/{competitionId}",
                new { title = "改名" },
                boss)));
    }

    [Fact]
    public async Task CompetitionScoringRankingAndTies()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        foreach (var name in new[] { "p01", "p02", "p03" })
        {
            await environment.SeedUserAsync(name);
        }
        using var bossClient = environment.CreateClient();
        var boss = await bossClient.LoginAsync("boss");
        var sessions = new Dictionary<string, (HttpClient Client, Dictionary<string, string> Headers)>();
        foreach (var name in new[] { "p01", "p02", "p03" })
        {
            var client = environment.CreateClient();
            sessions[name] = (client, await client.LoginAsync(name));
        }

        int competitionId;
        using (var competition = await CreateCompetitionAsync(bossClient, boss))
        {
            competitionId = competition.RootElement.GetProperty("id").GetInt32();
        }
        int requiredId;
        using (var task = await AddTaskAsync(bossClient, boss, competitionId, new Dictionary<string, object?>
        {
            ["title"] = "必做",
            ["sort_order"] = 1,
        }))
        {
            requiredId = task.RootElement.GetProperty("id").GetInt32();
        }
        int optionalId;
        using (var task = await AddTaskAsync(bossClient, boss, competitionId, new Dictionary<string, object?>
        {
            ["title"] = "选做",
            ["required"] = false,
            ["sort_order"] = 2,
            ["max_score"] = "50.00",
            ["weight"] = "5.00",
        }))
        {
            optionalId = task.RootElement.GetProperty("id").GetInt32();
        }
        await bossClient.PostActionAsync($"/api/v1/admin/competitions/{competitionId}/publish", boss);

        var submissions = new Dictionary<(string User, string TaskKind), int>();
        foreach (var (name, (userClient, userHeaders)) in sessions)
        {
            await RegisterAsync(userClient, userHeaders, competitionId);
            using var artifact = await userClient.PublishArtifactAsync(userHeaders, new { title = $"{name} 的必做成果" });
            using var submitted = await JoinAndSubmitAsync(
                userClient, userHeaders, requiredId, artifact.RootElement.GetProperty("id").GetInt32());
            submissions[(name, "required")] = submitted.RootElement.GetProperty("id").GetInt32();
        }
        // p03 skips the optional task entirely (0 points); p01 and p02 submit it.
        foreach (var name in new[] { "p01", "p02" })
        {
            var (userClient, userHeaders) = sessions[name];
            using var artifact = await userClient.PublishArtifactAsync(userHeaders, new { title = $"{name} 的选做成果" });
            using var submitted = await JoinAndSubmitAsync(
                userClient, userHeaders, optionalId, artifact.RootElement.GetProperty("id").GetInt32());
            submissions[(name, "optional")] = submitted.RootElement.GetProperty("id").GetInt32();
        }

        // A fourth participant missing the required task must not be ranked.
        await environment.SeedUserAsync("p04");
        using var p4Client = environment.CreateClient();
        var p4Headers = await p4Client.LoginAsync("p04");
        await RegisterAsync(p4Client, p4Headers, competitionId);
        using (var p4Artifact = await p4Client.PublishArtifactAsync(p4Headers, new { title = "p04 的选做成果" }))
        {
            using var submitted = await JoinAndSubmitAsync(
                p4Client, p4Headers, optionalId, p4Artifact.RootElement.GetProperty("id").GetInt32());
            submissions[("p04", "optional")] = submitted.RootElement.GetProperty("id").GetInt32();
        }

        Assert.Equal(
            HttpStatusCode.OK,
            (await ReviewAsync(bossClient, boss, submissions[("p01", "required")], "20.00")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await ReviewAsync(bossClient, boss, submissions[("p02", "required")], "20.00")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await ReviewAsync(bossClient, boss, submissions[("p03", "required")], "15.00")).StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            (await ReviewAsync(bossClient, boss, submissions[("p01", "optional")], "25.00")).StatusCode);
        // p02's optional review is still missing; publication is blocked once
        // the competition has ended (the end check runs before completeness).
        using (var ended = await bossClient.PatchJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}",
            new { end_at = "2026-01-01T00:00:00Z" },
            boss))
        {
            Assert.Equal(HttpStatusCode.OK, ended.StatusCode);
        }
        using (var incomplete = await bossClient.PostActionAsync(
            $"/api/v1/admin/competitions/{competitionId}/publish-results", boss))
        {
            Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
            Assert.Equal("COMPETITION_REVIEWS_INCOMPLETE", await CodeAsync(incomplete));
        }
        Assert.Equal(
            HttpStatusCode.OK,
            (await ReviewAsync(bossClient, boss, submissions[("p02", "optional")], "25.00")).StatusCode);

        // p01 and p02 tie on total (6.6667 + 2.5000), p03 follows, p04 unranked.
        var registrationIds = new Dictionary<string, int>();
        using (var registrationList = await bossClient.GetAsync(
            $"/api/v1/admin/competitions/{competitionId}/registrations", boss))
        {
            using var body = await registrationList.ReadJsonAsync();
            foreach (var row in body.RootElement.GetProperty("items").EnumerateArray())
            {
                registrationIds[row.GetProperty("user").GetProperty("username").GetString()!] =
                    row.GetProperty("id").GetInt32();
            }
        }
        using (var published = await bossClient.PostJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}/publish-results",
            new
            {
                awards = new[]
                {
                    new { registration_id = registrationIds["p01"], award = "一等奖" },
                    new { registration_id = registrationIds["p02"], award = "一等奖" },
                },
            },
            boss))
        {
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        }

        using (var leaderboard = await bossClient.GetAsync($"/api/v1/competitions/{competitionId}/results"))
        {
            using var body = await leaderboard.ReadJsonAsync();
            var ranked = body.RootElement.GetProperty("items").EnumerateArray()
                .Select(row => (
                    row.GetProperty("user").GetProperty("username").GetString()!,
                    row.GetProperty("total_score").GetString()!,
                    row.GetProperty("rank").GetInt32(),
                    row.GetProperty("award").ValueKind == JsonValueKind.Null
                        ? null
                        : row.GetProperty("award").GetString()))
                .ToList();
            Assert.Equal(
                new[]
                {
                    ("p01", "9.1667", 1, "一等奖"),
                    ("p02", "9.1667", 1, "一等奖"),
                    ("p03", "5.0000", 3, (string?)null),
                },
                ranked);
        }

        // Awards must map to ranked registrations exactly once.
        Assert.Equal(
            "COMPETITION_AWARD_INVALID",
            await CodeAsync(await bossClient.PostJsonAsync(
                $"/api/v1/admin/competitions/{competitionId}/publish-results",
                new { awards = new[] { new { registration_id = registrationIds["p04"], award = "参与奖" } } },
                boss)));

        // Republishing replaces the snapshot as a whole.
        using (var republished = await bossClient.PostJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}/publish-results",
            new { awards = new[] { new { registration_id = registrationIds["p03"], award = "三等奖" } } },
            boss))
        {
            Assert.Equal(HttpStatusCode.OK, republished.StatusCode);
        }
        using (var leaderboard = await bossClient.GetAsync($"/api/v1/competitions/{competitionId}/results"))
        {
            using var body = await leaderboard.ReadJsonAsync();
            var awards = body.RootElement.GetProperty("items").EnumerateArray()
                .ToDictionary(
                    row => row.GetProperty("user").GetProperty("username").GetString()!,
                    row => row.GetProperty("award").ValueKind == JsonValueKind.Null
                        ? null
                        : row.GetProperty("award").GetString());
            Assert.Equal(
                new Dictionary<string, string?> { ["p01"] = null, ["p02"] = null, ["p03"] = "三等奖" },
                awards);
        }
    }

    [Fact]
    public async Task RegistrationCancelAndConfigLock()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        await environment.SeedUserAsync("staffb");
        using var bossClient = environment.CreateClient();
        using var bClient = environment.CreateClient();
        var boss = await bossClient.LoginAsync("boss");
        var b = await bClient.LoginAsync("staffb");

        int competitionId;
        using (var competition = await CreateCompetitionAsync(bossClient, boss))
        {
            competitionId = competition.RootElement.GetProperty("id").GetInt32();
        }
        int taskId;
        using (var task = await AddTaskAsync(bossClient, boss, competitionId))
        {
            taskId = task.RootElement.GetProperty("id").GetInt32();
        }
        await bossClient.PostActionAsync($"/api/v1/admin/competitions/{competitionId}/publish", boss);

        // Cancel without a registration is a 404.
        Assert.Equal(
            "REGISTRATION_NOT_FOUND",
            await CodeAsync(await bClient.DeleteAsync(
                b, $"/api/v1/competitions/{competitionId}/registrations/me")));

        await RegisterAsync(bClient, b, competitionId);
        using (var cancelled = await bClient.DeleteAsync(
            b, $"/api/v1/competitions/{competitionId}/registrations/me"))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            using var body = await cancelled.ReadJsonAsync();
            Assert.Equal("CANCELLED", body.RootElement.GetProperty("status").GetString());
            Assert.NotNull(body.RootElement.GetProperty("cancelled_at").GetString());
        }
        using (var detail = await bClient.GetAsync($"/api/v1/competitions/{competitionId}"))
        {
            using var body = await detail.ReadJsonAsync();
            Assert.Equal(0, body.RootElement.GetProperty("registration_count").GetInt32());
        }

        // Re-registering reuses the same row.
        await RegisterAsync(bClient, b, competitionId);

        // Submissions block cancellation.
        int artifactId;
        using (var artifact = await bClient.PublishArtifactAsync(b, new { title = "锁定配置的成果" }))
        {
            artifactId = artifact.RootElement.GetProperty("id").GetInt32();
        }
        await JoinAndSubmitAsync(bClient, b, taskId, artifactId);
        Assert.Equal(
            "COMPETITION_SUBMISSION_EXISTS",
            await CodeAsync(await bClient.DeleteAsync(
                b, $"/api/v1/competitions/{competitionId}/registrations/me")));

        // Once submissions exist, scoring configuration freezes.
        Assert.Equal(
            "COMPETITION_CONFIG_LOCKED",
            await CodeAsync(await bossClient.PatchJsonAsync(
                $"/api/v1/admin/competitions/{competitionId}/tasks/{taskId}",
                new { max_score = "99.00" },
                boss)));
        using (var sortOnly = await bossClient.PatchJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}/tasks/{taskId}",
            new { sort_order = 5 },
            boss))
        {
            Assert.Equal(HttpStatusCode.OK, sortOnly.StatusCode);
        }
        // Configuration values are exposed through the competition task list.
        using (var listedTasks = await bossClient.GetAsync($"/api/v1/competitions/{competitionId}/tasks"))
        {
            using var body = await listedTasks.ReadJsonAsync();
            var item = body.RootElement.GetProperty("items")[0];
            Assert.Equal(5, item.GetProperty("sort_order").GetInt32());
            Assert.Equal("30.00", item.GetProperty("max_score").GetString());
            Assert.Equal(1, item.GetProperty("current_submission_count").GetInt32());
            Assert.Equal(0, item.GetProperty("reviewed_count").GetInt32());
        }
        Assert.Equal(
            "COMPETITION_CONFIG_LOCKED",
            await CodeAsync(await bossClient.DeleteAsync(
                boss, $"/api/v1/admin/competitions/{competitionId}/tasks/{taskId}")));

        // Competitions with references refuse physical deletion.
        Assert.Equal(
            "COMPETITION_HAS_REFERENCES",
            await CodeAsync(await bossClient.DeleteAsync(
                boss, $"/api/v1/admin/competitions/{competitionId}")));

        // A fresh draft allows full configuration and deletion.
        int draftId;
        using (var draft = await CreateCompetitionAsync(bossClient, boss, new { title = "草稿赛" }))
        {
            draftId = draft.RootElement.GetProperty("id").GetInt32();
        }
        int draftTaskId;
        using (var draftTask = await AddTaskAsync(bossClient, boss, draftId))
        {
            draftTaskId = draftTask.RootElement.GetProperty("id").GetInt32();
        }
        using (var patched = await bossClient.PatchJsonAsync(
            $"/api/v1/admin/competitions/{draftId}/tasks/{draftTaskId}",
            new { title = "改名任务", weight = "8.00", deadline_at = "2998-06-01T00:00:00Z" },
            boss))
        {
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        }
        using (var draftTasks = await bossClient.GetAsync($"/api/v1/competitions/{draftId}/tasks"))
        {
            using var body = await draftTasks.ReadJsonAsync();
            Assert.Equal("8.00", body.RootElement.GetProperty("items")[0].GetProperty("weight").GetString());
            Assert.Equal("改名任务", body.RootElement.GetProperty("items")[0].GetProperty("title").GetString());
        }
        Assert.Equal(
            "COMPETITION_TIME_CONFLICT",
            await CodeAsync(await bossClient.PatchJsonAsync(
                $"/api/v1/admin/competitions/{draftId}/tasks/{draftTaskId}",
                new { deadline_at = "2999-06-01T00:00:00Z" },
                boss)));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await bossClient.DeleteAsync(
                boss, $"/api/v1/admin/competitions/{draftId}/tasks/{draftTaskId}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await bossClient.DeleteAsync(boss, $"/api/v1/admin/competitions/{draftId}")).StatusCode);
    }

    [Fact]
    public async Task CompetitionTaskDisableAndReopen()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        await environment.SeedUserAsync("staffb");
        using var bossClient = environment.CreateClient();
        using var bClient = environment.CreateClient();
        var boss = await bossClient.LoginAsync("boss");
        var b = await bClient.LoginAsync("staffb");

        int competitionId;
        using (var competition = await CreateCompetitionAsync(bossClient, boss))
        {
            competitionId = competition.RootElement.GetProperty("id").GetInt32();
        }
        int taskId;
        using (var task = await AddTaskAsync(bossClient, boss, competitionId))
        {
            taskId = task.RootElement.GetProperty("id").GetInt32();
        }
        await bossClient.PostActionAsync($"/api/v1/admin/competitions/{competitionId}/publish", boss);
        await RegisterAsync(bClient, b, competitionId);
        // Join first: a disabled task still rejects non-participants with FORBIDDEN.
        Assert.Equal(
            HttpStatusCode.Created,
            (await bClient.PostActionAsync($"/api/v1/tasks/{taskId}/participants", b)).StatusCode);

        using (var closed = await bossClient.PostActionAsync($"/api/v1/tasks/{taskId}/close", boss))
        {
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            using var body = await closed.ReadJsonAsync();
            Assert.Equal("CLOSED", body.RootElement.GetProperty("status").GetString());
        }

        int artifactId;
        using (var artifact = await bClient.PublishArtifactAsync(b, new { title = "停用后的成果" }))
        {
            artifactId = artifact.RootElement.GetProperty("id").GetInt32();
        }
        using (var disabled = await bClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = artifactId },
            b))
        {
            Assert.Equal("COMPETITION_STATE_CONFLICT", await CodeAsync(disabled));
        }

        using (var reopened = await bossClient.PostActionAsync($"/api/v1/admin/tasks/{taskId}/reopen", boss))
        {
            Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
            using var body = await reopened.ReadJsonAsync();
            Assert.Equal("OPEN", body.RootElement.GetProperty("status").GetString());
        }

        using (var resubmitted = await bClient.PostJsonAsync(
            $"/api/v1/tasks/{taskId}/submissions",
            new { artifact_id = artifactId },
            b))
        {
            Assert.Equal(HttpStatusCode.Created, resubmitted.StatusCode);
        }

        // Leaving a competition task with submissions is still blocked.
        Assert.Equal(
            "TASK_SUBMISSION_EXISTS",
            await CodeAsync(await bClient.DeleteAsync(b, $"/api/v1/tasks/{taskId}/participants/me")));
    }

    [Fact]
    public async Task CompetitionConstraints()
    {
        using var environment = new TestEnvironment();
        using var connection = environment.OpenConnection();
        Execute(connection, "INSERT INTO users (username, display_name, password_hash, role, is_active, created_at, updated_at) "
            + "VALUES ('constraint', 'Constraint', 'x', 'SYSTEM_ADMIN', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Execute(connection, "INSERT INTO competitions (title, summary, rules_markdown, start_at, end_at, status, created_by, created_at, updated_at) "
            + "VALUES ('约束赛', 's', 'r', '2020-01-01 00:00:00', '2999-01-01 00:00:00', 'PUBLISHED', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Execute(connection, "INSERT INTO tasks (title, description, creator_id, status, competition_id, competition_required, competition_sort_order, competition_max_score, competition_weight, created_at, updated_at) "
            + "VALUES ('约束任务', 'd', 1, 'OPEN', 1, 1, 1, 30.00, 10.00, '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Execute(connection, "INSERT INTO artifacts (title, summary, content_markdown, author_id, status, created_at, updated_at) "
            + "VALUES ('约束展品', 's', '# c', 1, 'PUBLISHED', '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Execute(connection, "INSERT INTO task_participants (task_id, user_id, status, joined_at, created_at, updated_at) "
            + "VALUES (1, 1, 'ACTIVE', '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Execute(connection, "INSERT INTO competition_registrations (competition_id, user_id, status, registered_at, created_at, updated_at) "
            + "VALUES (1, 1, 'REGISTERED', '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Execute(connection, "INSERT INTO task_submissions (task_id, participant_id, artifact_id, round_no, status, is_current, submitted_at, created_at, updated_at) "
            + "VALUES (1, 1, 1, 1, 'SUBMITTED', 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')");

        // Duplicate registration and standalone tasks with competition columns.
        Assert.Throws<SqliteException>(() =>
            Execute(connection, "INSERT INTO competition_registrations (competition_id, user_id, status, registered_at, created_at, updated_at) "
                + "VALUES (1, 1, 'CANCELLED', '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')"));
        Assert.Throws<SqliteException>(() =>
            Execute(connection, "INSERT INTO tasks (title, description, creator_id, status, competition_max_score, created_at, updated_at) "
                + "VALUES ('独立任务带竞赛列', 'd', 1, 'OPEN', 10.00, '2026-01-01 00:00:00', '2026-01-01 00:00:00')"));

        // One review per submission (unique) and RESTRICT on referenced rows.
        Execute(connection, "INSERT INTO competition_reviews (task_submission_id, reviewer_id, raw_score, reviewed_at, created_at, updated_at) "
            + "VALUES (1, 1, 20.00, '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Assert.Throws<SqliteException>(() =>
            Execute(connection, "INSERT INTO competition_reviews (task_submission_id, reviewer_id, raw_score, reviewed_at, created_at, updated_at) "
                + "VALUES (1, 1, 21.00, '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')"));
        Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM task_submissions WHERE id = 1"));

        Execute(connection, "INSERT INTO competition_results (competition_id, registration_id, total_score, rank, published_by, published_at, created_at, updated_at) "
            + "VALUES (1, 1, 6.6667, 1, 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')");
        Assert.Throws<SqliteException>(() =>
            Execute(connection, "INSERT INTO competition_results (competition_id, registration_id, total_score, rank, published_by, published_at, created_at, updated_at) "
                + "VALUES (1, 1, 7.0000, 1, 1, '2026-01-01 00:00:00', '2026-01-01 00:00:00', '2026-01-01 00:00:00')"));
        Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM competition_registrations WHERE id = 1"));
        Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM users WHERE id = 1"));
        Assert.Throws<SqliteException>(() => Execute(connection, "DELETE FROM competitions WHERE id = 1"));
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
