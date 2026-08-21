using System.Net;

namespace MkAIHub.Api.Tests;

public sealed class AdminActionTests
{
    [Fact]
    public async Task CommentHideAndRestoreVisibility()
    {
        using var environment = new TestEnvironment();
        var adminId = await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        await environment.SeedUserAsync("author");
        await environment.SeedUserAsync("reader");

        using var author = environment.CreateClient();
        using var reader = environment.CreateClient();
        using var admin = environment.CreateClient();
        var authorHeaders = await author.LoginAsync("author");
        var readerHeaders = await reader.LoginAsync("reader");
        var adminHeaders = await admin.LoginAsync("boss");

        int artifactId;
        using (var draft = await author.CreateDraftAsync(authorHeaders))
        {
            artifactId = draft.RootElement.GetProperty("id").GetInt32();
        }
        using (var published = await StateActionAsync(author, authorHeaders, $"/api/v1/artifacts/{artifactId}/publish"))
        {
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        }

        int issueId;
        using (var issue = await reader.PostJsonAsync(
            "/api/v1/issues",
            new { title = "导出功能异常", description = "导出的文件缺少标题行。" },
            readerHeaders))
        {
            Assert.Equal(HttpStatusCode.Created, issue.StatusCode);
            using var body = await issue.ReadJsonAsync();
            issueId = body.RootElement.GetProperty("id").GetInt32();
        }

        int artifactCommentId;
        using (var artifactComment = await reader.PostJsonAsync(
            $"/api/v1/artifacts/{artifactId}/comments",
            new { content = "希望补充示例。" },
            readerHeaders))
        {
            Assert.Equal(HttpStatusCode.Created, artifactComment.StatusCode);
            using var body = await artifactComment.ReadJsonAsync();
            artifactCommentId = body.RootElement.GetProperty("id").GetInt32();
        }
        int issueCommentId;
        using (var issueComment = await author.PostJsonAsync(
            $"/api/v1/issues/{issueId}/comments",
            new { content = "已在新版本修复。" },
            authorHeaders))
        {
            Assert.Equal(HttpStatusCode.Created, issueComment.StatusCode);
            using var body = await issueComment.ReadJsonAsync();
            issueCommentId = body.RootElement.GetProperty("id").GetInt32();
        }

        // Only administrators may moderate comments.
        using (var forbiddenHide = await StateActionAsync(
            reader, readerHeaders, $"/api/v1/admin/comments/{artifactCommentId}/hide"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenHide.StatusCode);
        }
        using (var missingHide = await StateActionAsync(admin, adminHeaders, "/api/v1/admin/comments/999/hide"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missingHide.StatusCode);
        }

        using (var hideArtifactComment = await StateActionAsync(
            admin, adminHeaders, $"/api/v1/admin/comments/{artifactCommentId}/hide"))
        {
            Assert.Equal(HttpStatusCode.NoContent, hideArtifactComment.StatusCode);
        }
        using (var hideIssueComment = await StateActionAsync(
            admin, adminHeaders, $"/api/v1/admin/comments/{issueCommentId}/hide"))
        {
            Assert.Equal(HttpStatusCode.NoContent, hideIssueComment.StatusCode);
        }

        // Employees no longer see the hidden comments, administrators still do.
        using (var employeeArtifactComments = await reader.GetAsync($"/api/v1/artifacts/{artifactId}/comments"))
        {
            using var body = await employeeArtifactComments.ReadJsonAsync();
            Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var employeeIssueComments = await reader.GetAsync($"/api/v1/issues/{issueId}/comments"))
        {
            using var body = await employeeIssueComments.ReadJsonAsync();
            Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        }

        using (var adminArtifactComments = await admin.GetAsync($"/api/v1/artifacts/{artifactId}/comments"))
        {
            using var body = await adminArtifactComments.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(
                "HIDDEN",
                body.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        }
        using (var adminIssueComments = await admin.GetAsync($"/api/v1/issues/{issueId}/comments"))
        {
            using var body = await adminIssueComments.ReadJsonAsync();
            Assert.Equal(
                "HIDDEN",
                body.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        }

        using (var restore = await StateActionAsync(
            admin, adminHeaders, $"/api/v1/admin/comments/{artifactCommentId}/restore"))
        {
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        }
        using (var restored = await reader.GetAsync($"/api/v1/artifacts/{artifactId}/comments"))
        {
            using var body = await restored.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
            Assert.Equal(
                "VISIBLE",
                body.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
        }

        var hideRecords = environment.Audit.Records
            .Where(record => record.Action == "admin.comment.hide")
            .ToList();
        Assert.Equal(2, hideRecords.Count);
        Assert.Equal(
            new HashSet<int> { artifactCommentId, issueCommentId },
            hideRecords.Select(record => record.TargetId).ToHashSet());
        Assert.All(hideRecords, record => Assert.Equal("comment", record.TargetType));
        Assert.All(hideRecords, record => Assert.Equal(adminId, record.ActorId));
        var restoreRecords = environment.Audit.Records
            .Where(record => record.Action == "admin.comment.restore")
            .ToList();
        Assert.Single(restoreRecords);
        Assert.Equal(artifactCommentId, restoreRecords[0].TargetId);
    }

    [Fact]
    public async Task AdminCompetitionAndUserActionsAreLogged()
    {
        using var environment = new TestEnvironment();
        var adminId = await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        using var client = environment.CreateClient();
        var adminHeaders = await client.LoginAsync("boss");

        int competitionId;
        using (var competition = await client.PostJsonAsync(
            "/api/v1/admin/competitions",
            new
            {
                title = "内部提示词大赛",
                summary = "评选最佳内部提示词。",
                rules_markdown = "# 规则",
                start_at = "2998-01-01T00:00:00Z",
                end_at = "2999-01-01T00:00:00Z",
            },
            adminHeaders))
        {
            Assert.Equal(HttpStatusCode.Created, competition.StatusCode);
            using var body = await competition.ReadJsonAsync();
            competitionId = body.RootElement.GetProperty("id").GetInt32();
        }

        int createdUserId;
        using (var createdUser = await client.PostJsonAsync(
            "/api/v1/admin/users",
            new
            {
                username = "newcomer",
                display_name = "Newcomer",
                password = "password1",
                role = "EMPLOYEE",
            },
            adminHeaders))
        {
            Assert.Equal(HttpStatusCode.Created, createdUser.StatusCode);
            using var body = await createdUser.ReadJsonAsync();
            createdUserId = body.RootElement.GetProperty("id").GetInt32();
        }

        var competitionCreate = environment.Audit.Records
            .Where(record => record.Action == "admin.competition.create")
            .ToList();
        Assert.Single(competitionCreate);
        Assert.Equal(adminId, competitionCreate[0].ActorId);
        Assert.Equal("competition", competitionCreate[0].TargetType);
        Assert.Equal(competitionId, competitionCreate[0].TargetId);

        var userCreate = environment.Audit.Records
            .Where(record => record.Action == "admin.user.create")
            .ToList();
        Assert.Single(userCreate);
        Assert.Equal(createdUserId, userCreate[0].TargetId);
        Assert.Equal("newcomer", userCreate[0].Details["username"]);
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
