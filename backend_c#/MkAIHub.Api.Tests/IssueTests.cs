using System.Net;

namespace MkAIHub.Api.Tests;

public sealed class IssueTests
{
    private static async Task<JsonDocument> CreateIssueAsync(
        HttpClient client,
        Dictionary<string, string> headers,
        object? overrides = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "希望支持导出 Markdown",
            ["description"] = "详情页增加导出按钮。",
        };
        if (overrides is Dictionary<string, object?> additional)
        {
            foreach (var (key, value) in additional)
            {
                payload[key] = value;
            }
        }
        using var response = await client.PostJsonAsync("/api/v1/issues", payload, headers);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"create issue failed: {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text);
    }

    [Fact]
    public async Task IssueLifecycleCommentsAndPermissions()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("author");
        await environment.SeedUserAsync("other");
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        using var client = environment.CreateClient();
        var authorHeaders = await client.LoginAsync("author");

        int issueId;
        using (var issue = await CreateIssueAsync(client, authorHeaders))
        {
            issueId = issue.RootElement.GetProperty("id").GetInt32();
            Assert.Equal("OPEN", issue.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                "author",
                issue.RootElement.GetProperty("author").GetProperty("username").GetString());
        }

        using (var updated = await client.PatchJsonAsync(
            $"/api/v1/issues/{issueId}",
            new { description = "详情页增加导出按钮，支持附件一起打包。" },
            authorHeaders))
        {
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }

        int commentId;
        using (var other = environment.CreateClient())
        {
            var otherHeaders = await other.LoginAsync("other");
            using (var visible = await other.GetAsync($"/api/v1/issues/{issueId}"))
            {
                Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
            }
            using (var forbiddenEdit = await other.PatchJsonAsync(
                $"/api/v1/issues/{issueId}",
                new { title = "越权修改" },
                otherHeaders))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenEdit.StatusCode);
            }
            using (var forbiddenClose = await StateActionAsync(
                other, otherHeaders, $"/api/v1/issues/{issueId}/close"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenClose.StatusCode);
            }

            using (var comment = await other.PostJsonAsync(
                $"/api/v1/issues/{issueId}/comments",
                new { content = "支持这个建议。" },
                otherHeaders))
            {
                Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
                using var body = await comment.ReadJsonAsync();
                commentId = body.RootElement.GetProperty("id").GetInt32();
                Assert.Equal(issueId, body.RootElement.GetProperty("issue_id").GetInt32());
                Assert.True(body.RootElement.GetProperty("artifact_id").ValueKind == JsonValueKind.Null);
                Assert.EndsWith("Z", body.RootElement.GetProperty("created_at").GetString());
            }
            using (var authorComment = await client.PostJsonAsync(
                $"/api/v1/issues/{issueId}/comments",
                new { content = "感谢反馈。" },
                authorHeaders))
            {
                Assert.Equal(HttpStatusCode.Created, authorComment.StatusCode);
            }

            using (var comments = await other.GetAsync($"/api/v1/issues/{issueId}/comments"))
            {
                Assert.Equal(HttpStatusCode.OK, comments.StatusCode);
                using var body = await comments.ReadJsonAsync();
                Assert.Equal(2, body.RootElement.GetProperty("total").GetInt32());
            }

            using (var forbiddenDelete = await DeleteWithHeadersAsync(
                client, authorHeaders, $"/api/v1/comments/{commentId}"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);
            }
            using (var ownDelete = await DeleteWithHeadersAsync(
                other, otherHeaders, $"/api/v1/comments/{commentId}"))
            {
                Assert.Equal(HttpStatusCode.NoContent, ownDelete.StatusCode);
            }
        }

        using (var closed = await StateActionAsync(client, authorHeaders, $"/api/v1/issues/{issueId}/close"))
        {
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            using var body = await closed.ReadJsonAsync();
            Assert.Equal("CLOSED", body.RootElement.GetProperty("status").GetString());
            Assert.EndsWith("Z", body.RootElement.GetProperty("closed_at").GetString()!);
        }
        using (var editClosed = await client.PatchJsonAsync(
            $"/api/v1/issues/{issueId}",
            new { title = "关闭后不可编辑" },
            authorHeaders))
        {
            Assert.Equal(HttpStatusCode.Conflict, editClosed.StatusCode);
        }
        using (var commentClosed = await client.PostJsonAsync(
            $"/api/v1/issues/{issueId}/comments",
            new { content = "关闭后不可评论" },
            authorHeaders))
        {
            Assert.Equal(HttpStatusCode.Conflict, commentClosed.StatusCode);
        }
        using (var closeAgain = await StateActionAsync(client, authorHeaders, $"/api/v1/issues/{issueId}/close"))
        {
            Assert.Equal(HttpStatusCode.Conflict, closeAgain.StatusCode);
        }

        using (var admin = environment.CreateClient())
        {
            var adminHeaders = await admin.LoginAsync("boss");
            using (var reopened = await StateActionAsync(admin, adminHeaders, $"/api/v1/issues/{issueId}/reopen"))
            {
                Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
                using var body = await reopened.ReadJsonAsync();
                Assert.Equal("OPEN", body.RootElement.GetProperty("status").GetString());
                Assert.True(body.RootElement.GetProperty("closed_at").ValueKind == JsonValueKind.Null);
            }
        }

        using (var reopenedComment = await client.PostJsonAsync(
            $"/api/v1/issues/{issueId}/comments",
            new { content = "重新开放后可以继续讨论。" },
            authorHeaders))
        {
            Assert.Equal(HttpStatusCode.Created, reopenedComment.StatusCode);
        }
        using (var reopenOpen = await StateActionAsync(client, authorHeaders, $"/api/v1/issues/{issueId}/reopen"))
        {
            Assert.Equal(HttpStatusCode.Conflict, reopenOpen.StatusCode);
        }

        using (var listing = await client.GetAsync("/api/v1/issues"))
        {
            Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
            using var body = await listing.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var mine = await client.GetAsync("/api/v1/issues?mine=true"))
        {
            using var body = await mine.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var open = await client.GetAsync("/api/v1/issues?status=OPEN"))
        {
            using var body = await open.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var closedFilter = await client.GetAsync("/api/v1/issues?status=CLOSED"))
        {
            using var body = await closedFilter.ReadJsonAsync();
            Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        }
    }

    [Fact]
    public async Task IssueValidationAndSearch()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("author");
        using var client = environment.CreateClient();
        var headers = await client.LoginAsync("author");

        using (var blankTitle = await client.PostJsonAsync(
            "/api/v1/issues",
            new { title = "", description = "正文" },
            headers))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blankTitle.StatusCode);
        }
        using (var unexpected = await client.PostJsonAsync(
            "/api/v1/issues",
            new { title = "标题", description = "正文", unexpected = true },
            headers))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unexpected.StatusCode);
        }
        using (var missing = await client.GetAsync("/api/v1/issues/99999"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        await CreateIssueAsync(
            client,
            headers,
            new Dictionary<string, object?> { ["title"] = "搜索关键词甲", ["description"] = "正文一" });
        await CreateIssueAsync(
            client,
            headers,
            new Dictionary<string, object?> { ["title"] = "普通标题", ["description"] = "包含关键词乙的正文" });
        using (var first = await client.GetAsync("/api/v1/issues?q=%E5%85%B3%E9%94%AE%E8%AF%8D%E7%94%B2"))
        {
            using var body = await first.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var second = await client.GetAsync("/api/v1/issues?q=%E5%85%B3%E9%94%AE%E8%AF%8D%E4%B9%99"))
        {
            using var body = await second.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
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
