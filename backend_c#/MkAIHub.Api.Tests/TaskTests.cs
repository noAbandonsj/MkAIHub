using System.Net;

namespace MkAIHub.Api.Tests;

public sealed class TaskTests
{
    private static async Task<JsonDocument> CreateTaskAsync(
        HttpClient client,
        Dictionary<string, string> headers,
        object? overrides = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "整理内部提示词清单",
            ["description"] = "收集各组常用提示词并归类整理。",
            ["deadline_at"] = null,
        };
        if (overrides is Dictionary<string, object?> additional)
        {
            foreach (var (key, value) in additional)
            {
                payload[key] = value;
            }
        }
        using var response = await client.PostJsonAsync("/api/v1/tasks", payload, headers);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"create task failed: {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text);
    }

    [Fact]
    public async Task TaskLifecycleAndPermissions()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("creator");
        await environment.SeedUserAsync("other");
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        using var client = environment.CreateClient();
        var creatorHeaders = await client.LoginAsync("creator");

        int taskId;
        using (var task = await CreateTaskAsync(
            client,
            creatorHeaders,
            new Dictionary<string, object?> { ["deadline_at"] = "2026-09-01T12:00:00Z" }))
        {
            taskId = task.RootElement.GetProperty("id").GetInt32();
            Assert.Equal("OPEN", task.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                "creator",
                task.RootElement.GetProperty("creator").GetProperty("username").GetString());
            Assert.Equal(
                "2026-09-01T12:00:00Z",
                task.RootElement.GetProperty("deadline_at").GetString());
            Assert.EndsWith("Z", task.RootElement.GetProperty("created_at").GetString());
        }

        using (var updated = await client.PatchJsonAsync(
            $"/api/v1/tasks/{taskId}",
            new { title = "整理内部提示词清单（更新）" },
            creatorHeaders))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }
        using (var cleared = await client.PatchJsonAsync(
            $"/api/v1/tasks/{taskId}",
            new { deadline_at = (string?)null },
            creatorHeaders))
        {
            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
            using var body = await cleared.ReadJsonAsync();
            Assert.True(body.RootElement.GetProperty("deadline_at").ValueKind == JsonValueKind.Null);
        }

        using (var other = environment.CreateClient())
        {
            var otherHeaders = await other.LoginAsync("other");
            using (var visible = await other.GetAsync($"/api/v1/tasks/{taskId}"))
            {
                Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
            }
            using (var forbiddenEdit = await other.PatchJsonAsync(
                $"/api/v1/tasks/{taskId}",
                new { title = "越权修改" },
                otherHeaders))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenEdit.StatusCode);
            }
            using (var forbiddenComplete = await StateActionAsync(
                other, otherHeaders, $"/api/v1/tasks/{taskId}/complete"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenComplete.StatusCode);
            }
            using (var forbiddenClose = await StateActionAsync(
                other, otherHeaders, $"/api/v1/tasks/{taskId}/close"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenClose.StatusCode);
            }
        }

        using (var completed = await StateActionAsync(client, creatorHeaders, $"/api/v1/tasks/{taskId}/complete"))
        {
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
            using var body = await completed.ReadJsonAsync();
            Assert.Equal("COMPLETED", body.RootElement.GetProperty("status").GetString());
            Assert.NotNull(body.RootElement.GetProperty("completed_at").GetString());
            Assert.EndsWith("Z", body.RootElement.GetProperty("completed_at").GetString()!);
        }
        using (var editAfterComplete = await client.PatchJsonAsync(
            $"/api/v1/tasks/{taskId}",
            new { title = "完成后不可编辑" },
            creatorHeaders))
        {
            Assert.Equal(HttpStatusCode.Conflict, editAfterComplete.StatusCode);
        }
        using (var completeAgain = await StateActionAsync(client, creatorHeaders, $"/api/v1/tasks/{taskId}/complete"))
        {
            Assert.Equal(HttpStatusCode.Conflict, completeAgain.StatusCode);
        }
        using (var closeCompleted = await StateActionAsync(client, creatorHeaders, $"/api/v1/tasks/{taskId}/close"))
        {
            Assert.Equal(HttpStatusCode.Conflict, closeCompleted.StatusCode);
        }

        int closableId;
        using (var closable = await CreateTaskAsync(
            client,
            creatorHeaders,
            new Dictionary<string, object?>
            {
                ["title"] = "将被管理员关闭的任务",
                ["description"] = "内容不再需要。",
            }))
        {
            closableId = closable.RootElement.GetProperty("id").GetInt32();
        }
        using (var admin = environment.CreateClient())
        {
            var adminHeaders = await admin.LoginAsync("boss");
            using (var closed = await StateActionAsync(admin, adminHeaders, $"/api/v1/tasks/{closableId}/close"))
            {
                Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
                using var body = await closed.ReadJsonAsync();
                Assert.Equal("CLOSED", body.RootElement.GetProperty("status").GetString());
                Assert.NotNull(body.RootElement.GetProperty("closed_at").GetString());
            }
            using (var closeAgain = await StateActionAsync(client, creatorHeaders, $"/api/v1/tasks/{closableId}/close"))
            {
                Assert.Equal(HttpStatusCode.Conflict, closeAgain.StatusCode);
            }
            using (var editClosed = await client.PatchJsonAsync(
                $"/api/v1/tasks/{closableId}",
                new { title = "关闭后不可编辑" },
                creatorHeaders))
            {
                Assert.Equal(HttpStatusCode.Conflict, editClosed.StatusCode);
            }
        }

        using (var listing = await client.GetAsync("/api/v1/tasks"))
        {
            Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
            using var body = await listing.ReadJsonAsync();
            Assert.Equal(2, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var mine = await client.GetAsync("/api/v1/tasks?mine=true"))
        {
            using var body = await mine.ReadJsonAsync();
            Assert.Equal(2, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var open = await client.GetAsync("/api/v1/tasks?status=OPEN"))
        {
            using var body = await open.ReadJsonAsync();
            Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var completedFilter = await client.GetAsync("/api/v1/tasks?status=COMPLETED"))
        {
            using var body = await completedFilter.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var search = await client.GetAsync("/api/v1/tasks?q=%E7%AE%A1%E7%90%86%E5%91%98"))
        {
            using var body = await search.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var detail = await client.GetAsync($"/api/v1/tasks/{taskId}"))
        {
            using var body = await detail.ReadJsonAsync();
            Assert.NotNull(body.RootElement.GetProperty("description").GetString());
        }
    }

    [Fact]
    public async Task TaskDeadlineRequiresUtcOffset()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("creator");
        using var client = environment.CreateClient();
        var headers = await client.LoginAsync("creator");

        using (var naive = await client.PostJsonAsync(
            "/api/v1/tasks",
            new
            {
                title = "无时区截止时间",
                description = "正文",
                deadline_at = "2026-09-01T12:00:00",
            },
            headers))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, naive.StatusCode);
        }

        using (var converted = await CreateTaskAsync(
            client,
            headers,
            new Dictionary<string, object?> { ["deadline_at"] = "2026-09-01T20:00:00+08:00" }))
        {
            Assert.Equal(
                "2026-09-01T12:00:00Z",
                converted.RootElement.GetProperty("deadline_at").GetString());
            var convertedId = converted.RootElement.GetProperty("id").GetInt32();

            using var update = await client.PatchJsonAsync(
                $"/api/v1/tasks/{convertedId}",
                new { deadline_at = "2026-09-02T09:00:00" },
                headers);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, update.StatusCode);
        }
    }

    [Fact]
    public async Task TaskValidationAndNotFound()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("creator");
        using var client = environment.CreateClient();
        var headers = await client.LoginAsync("creator");

        using (var blankTitle = await client.PostJsonAsync(
            "/api/v1/tasks",
            new { title = "   ", description = "正文" },
            headers))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blankTitle.StatusCode);
        }
        using (var extraField = await client.PostJsonAsync(
            "/api/v1/tasks",
            new { title = "标题", description = "正文", extra = true },
            headers))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, extraField.StatusCode);
        }

        using var task = await CreateTaskAsync(client, headers);
        var taskId = task.RootElement.GetProperty("id").GetInt32();
        using (var nullTitle = await client.PatchJsonAsync(
            $"/api/v1/tasks/{taskId}",
            new { title = (string?)null },
            headers))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, nullTitle.StatusCode);
        }
        using (var missing = await client.GetAsync("/api/v1/tasks/99999"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> StateActionAsync(
        HttpClient client,
        Dictionary<string, string> headers,
        string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        foreach (var (key, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }
        return await client.SendAsync(request);
    }
}
