using System.Net;

namespace MkAIHub.Api.Tests;

public sealed class CompetitionTests
{
    private static async Task<JsonDocument> CreateCompetitionAsync(
        HttpClient client,
        Dictionary<string, string> headers,
        object? overrides = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "内部提示词大赛",
            ["summary"] = "以季度为单位评选最佳内部提示词。",
            ["rules_markdown"] = "# 规则\n\n每人限提交一份作品。",
            ["start_at"] = "2998-01-01T00:00:00Z",
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

    [Fact]
    public async Task CompetitionLifecycleStatusAndPermissions()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        await environment.SeedUserAsync("staff");
        using var client = environment.CreateClient();
        var adminHeaders = await client.LoginAsync("boss");

        int upcomingId;
        using (var upcoming = await CreateCompetitionAsync(client, adminHeaders))
        {
            upcomingId = upcoming.RootElement.GetProperty("id").GetInt32();
            Assert.Equal("UPCOMING", upcoming.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                "boss",
                upcoming.RootElement.GetProperty("creator").GetProperty("username").GetString());
            Assert.Equal(
                "2998-01-01T00:00:00Z",
                upcoming.RootElement.GetProperty("start_at").GetString());
            Assert.EndsWith("Z", upcoming.RootElement.GetProperty("created_at").GetString());
        }

        int ongoingId;
        using (var ongoing = await CreateCompetitionAsync(
            client,
            adminHeaders,
            new Dictionary<string, object?>
            {
                ["title"] = "进行中的竞赛",
                ["start_at"] = "2020-01-01T00:00:00Z",
                ["end_at"] = "2999-01-01T00:00:00Z",
            }))
        {
            ongoingId = ongoing.RootElement.GetProperty("id").GetInt32();
            Assert.Equal("ONGOING", ongoing.RootElement.GetProperty("status").GetString());
        }
        using (var ended = await CreateCompetitionAsync(
            client,
            adminHeaders,
            new Dictionary<string, object?>
            {
                ["title"] = "已结束的竞赛",
                ["start_at"] = "2019-01-01T00:00:00Z",
                ["end_at"] = "2020-01-01T00:00:00Z",
            }))
        {
            Assert.Equal("ENDED", ended.RootElement.GetProperty("status").GetString());
        }

        using (var staff = environment.CreateClient())
        {
            var staffHeaders = await staff.LoginAsync("staff");

            // Employees are read-only: they browse but never maintain competitions.
            using (var forbiddenCreate = await staff.PostJsonAsync(
                "/api/v1/admin/competitions",
                new
                {
                    title = "越权创建",
                    summary = "员工不能创建竞赛",
                    rules_markdown = "规则",
                    start_at = "2998-01-01T00:00:00Z",
                    end_at = "2999-01-01T00:00:00Z",
                },
                staffHeaders))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreate.StatusCode);
            }
            using (var forbiddenUpdate = await staff.PatchJsonAsync(
                $"/api/v1/admin/competitions/{upcomingId}",
                new { title = "越权编辑" },
                staffHeaders))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenUpdate.StatusCode);
            }
            using (var forbiddenDelete = await DeleteWithHeadersAsync(
                staff, staffHeaders, $"/api/v1/admin/competitions/{upcomingId}"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);
            }

            using (var listed = await staff.GetAsync("/api/v1/competitions"))
            {
                Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
                using var body = await listed.ReadJsonAsync();
                Assert.Equal(3, body.RootElement.GetProperty("total").GetInt32());
                var statuses = body.RootElement.GetProperty("items")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("status").GetString() ?? string.Empty)
                    .ToHashSet();
                Assert.Equal(new HashSet<string> { "UPCOMING", "ONGOING", "ENDED" }, statuses);
                // Ordered by start_at descending, so the furthest-start competition leads.
                Assert.Equal(
                    "内部提示词大赛",
                    body.RootElement.GetProperty("items")[0].GetProperty("title").GetString());
            }

            using (var searched = await staff.GetAsync("/api/v1/competitions?q=%E5%B7%B2%E7%BB%93%E6%9D%9F"))
            {
                Assert.Equal(HttpStatusCode.OK, searched.StatusCode);
                using var body = await searched.ReadJsonAsync();
                Assert.Equal(
                    new[] { "已结束的竞赛" },
                    body.RootElement.GetProperty("items")
                        .EnumerateArray()
                        .Select(item => item.GetProperty("title").GetString())
                        .ToArray());
            }

            using (var detail = await staff.GetAsync($"/api/v1/competitions/{ongoingId}"))
            {
                Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
                using var body = await detail.ReadJsonAsync();
                Assert.Equal("ONGOING", body.RootElement.GetProperty("status").GetString());
                Assert.Contains("# 规则", body.RootElement.GetProperty("rules_markdown").GetString());
            }

            using (var missing = await staff.GetAsync("/api/v1/competitions/999"))
            {
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            }
        }
    }

    [Fact]
    public async Task CompetitionTimeValidation()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        using var client = environment.CreateClient();
        var adminHeaders = await client.LoginAsync("boss");

        var invalidWindows = new List<Dictionary<string, object?>>
        {
            new() { ["start_at"] = "2999-01-02T00:00:00Z", ["end_at"] = "2999-01-01T00:00:00Z" },
            new() { ["start_at"] = "2999-01-01T00:00:00Z", ["end_at"] = "2999-01-01T00:00:00Z" },
            new() { ["start_at"] = "2999-01-01T00:00:00" },
        };
        foreach (var overrides in invalidWindows)
        {
            using var response = await client.PostJsonAsync(
                "/api/v1/admin/competitions",
                MergePayload(overrides),
                adminHeaders);
            Assert.True(
                response.StatusCode == HttpStatusCode.UnprocessableEntity,
                $"expected 422 for {string.Join(",", overrides)}, got {(int)response.StatusCode}");
        }

        int competitionId;
        using (var competition = await CreateCompetitionAsync(client, adminHeaders))
        {
            competitionId = competition.RootElement.GetProperty("id").GetInt32();
        }

        // Patching only end_at can still invert the stored window.
        using (var inverted = await client.PatchJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}",
            new { end_at = "2997-01-01T00:00:00Z" },
            adminHeaders))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, inverted.StatusCode);
            using var body = await inverted.ReadJsonAsync();
            Assert.Equal("COMPETITION_TIME_CONFLICT", body.RootElement.GetProperty("code").GetString());
        }

        using (var nullTitle = await client.PatchJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}",
            new { title = (string?)null },
            adminHeaders))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, nullTitle.StatusCode);
        }

        using (var updated = await client.PatchJsonAsync(
            $"/api/v1/admin/competitions/{competitionId}",
            new
            {
                title = "内部提示词大赛（更新）",
                end_at = "2996-01-01T00:00:00Z",
                start_at = "2995-01-01T00:00:00Z",
            },
            adminHeaders))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            using var body = await updated.ReadJsonAsync();
            Assert.Equal("内部提示词大赛（更新）", body.RootElement.GetProperty("title").GetString());
            Assert.Equal("UPCOMING", body.RootElement.GetProperty("status").GetString());
        }

        using (var deleted = await DeleteWithHeadersAsync(
            client, adminHeaders, $"/api/v1/admin/competitions/{competitionId}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
        using (var missing = await client.GetAsync($"/api/v1/competitions/{competitionId}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    private static Dictionary<string, object?> MergePayload(Dictionary<string, object?> overrides)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "非法时间窗口",
            ["summary"] = "开始时间不得晚于结束时间",
            ["rules_markdown"] = "规则",
            ["start_at"] = "2998-01-01T00:00:00Z",
            ["end_at"] = "2999-01-01T00:00:00Z",
        };
        foreach (var (key, value) in overrides)
        {
            payload[key] = value;
        }
        return payload;
    }

    private static async Task<HttpResponseMessage> DeleteWithHeadersAsync(
        HttpClient client,
        Dictionary<string, string> headers,
        string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        foreach (var (key, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }
        return await client.SendAsync(request);
    }
}
