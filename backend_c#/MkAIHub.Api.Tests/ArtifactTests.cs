using System.Net;
using System.Text;

namespace MkAIHub.Api.Tests;

public sealed class ArtifactTests
{
    [Fact]
    public async Task ArtifactAttachmentPublishCommentAndVisibility()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("author");
        await environment.SeedUserAsync("reader");
        using var client = environment.CreateClient();
        var authorHeaders = await client.LoginAsync("author");

        using (var rejected = await client.SendAsync(
            UploadRequest(authorHeaders, "unsafe.exe", "not executable", "application/octet-stream")))
        {
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, rejected.StatusCode);
        }

        using var uploaded = await client.SendAsync(
            UploadRequest(authorHeaders, "guide.md", "# Internal guide", "text/markdown"));
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        using var uploadBody = await uploaded.ReadJsonAsync();
        var fileId = uploadBody.RootElement.GetProperty("id").GetInt32();

        int artifactId;
        using (var draft = await client.CreateDraftAsync(
            authorHeaders,
            new Dictionary<string, object?> { ["file_ids"] = new List<int> { fileId } }))
        {
            artifactId = draft.RootElement.GetProperty("id").GetInt32();
            Assert.Equal("DRAFT", draft.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                "guide.md",
                draft.RootElement.GetProperty("files")[0].GetProperty("original_name").GetString());
        }

        using (var mine = await client.GetAsync("/api/v1/artifacts?mine=true"))
        {
            using var body = await mine.ReadJsonAsync();
            Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
        }
        using (var published = await client.GetAsync("/api/v1/artifacts"))
        {
            using var body = await published.ReadJsonAsync();
            Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        }

        using (var reader = environment.CreateClient())
        {
            var readerHeaders = await reader.LoginAsync("reader");

            using (var hidden = await reader.GetAsync($"/api/v1/artifacts/{artifactId}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            }
            using (var hiddenDownload = await reader.GetAsync($"/api/v1/files/{fileId}/download"))
            {
                Assert.Equal(HttpStatusCode.NotFound, hiddenDownload.StatusCode);
            }

            using (var publishRequest = StateActionRequest(authorHeaders, $"/api/v1/artifacts/{artifactId}/publish"))
            using (var publishedNow = await client.SendAsync(publishRequest))
            {
                Assert.Equal(HttpStatusCode.OK, publishedNow.StatusCode);
                using var body = await publishedNow.ReadJsonAsync();
                Assert.Equal("PUBLISHED", body.RootElement.GetProperty("status").GetString());
            }

            using (var listing = await reader.GetAsync("/api/v1/artifacts?q=%E6%8F%90%E7%A4%BA%E8%AF%8D"))
            {
                Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
                using var body = await listing.ReadJsonAsync();
                Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
            }
            using (var visible = await reader.GetAsync($"/api/v1/artifacts/{artifactId}"))
            {
                Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
            }
            using (var download = await reader.GetAsync($"/api/v1/files/{fileId}/download"))
            {
                Assert.Equal(HttpStatusCode.OK, download.StatusCode);
                var content = await download.Content.ReadAsByteArrayAsync();
                Assert.Equal("# Internal guide", Encoding.UTF8.GetString(content));
            }

            using (var forbiddenEdit = await reader.PatchJsonAsync(
                $"/api/v1/artifacts/{artifactId}",
                new { title = "不允许的修改" },
                readerHeaders))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenEdit.StatusCode);
            }
            using (var archiveRequest = StateActionRequest(readerHeaders, $"/api/v1/artifacts/{artifactId}/archive"))
            using (var forbiddenArchive = await reader.SendAsync(archiveRequest))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenArchive.StatusCode);
            }

            int commentId;
            using (var comment = await reader.PostJsonAsync(
                $"/api/v1/artifacts/{artifactId}/comments",
                new { content = "已经成功复用。" },
                readerHeaders))
            {
                Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
                using var body = await comment.ReadJsonAsync();
                commentId = body.RootElement.GetProperty("id").GetInt32();
            }
            using (var comments = await client.GetAsync($"/api/v1/artifacts/{artifactId}/comments"))
            {
                using var body = await comments.ReadJsonAsync();
                Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
            }

            using (var deleteRequest = DeleteRequest(authorHeaders, $"/api/v1/comments/{commentId}"))
            using (var forbiddenDelete = await client.SendAsync(deleteRequest))
            {
                Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);
            }
            using (var ownDeleteRequest = DeleteRequest(readerHeaders, $"/api/v1/comments/{commentId}"))
            using (var ownDelete = await reader.SendAsync(ownDeleteRequest))
            {
                Assert.Equal(HttpStatusCode.NoContent, ownDelete.StatusCode);
            }
        }

        using (var archiveRequest = StateActionRequest(authorHeaders, $"/api/v1/artifacts/{artifactId}/archive"))
        using (var archived = await client.SendAsync(archiveRequest))
        {
            Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
            using var body = await archived.ReadJsonAsync();
            Assert.Equal("ARCHIVED", body.RootElement.GetProperty("status").GetString());
        }
        using (var listing = await client.GetAsync("/api/v1/artifacts"))
        {
            using var body = await listing.ReadJsonAsync();
            Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        }
        // Only administrators can use a status filter to browse archived content.
        using (var readerAgain = environment.CreateClient())
        {
            var readerHeadersAgain = await readerAgain.LoginAsync("reader");
            using (var employeeArchived = await readerAgain.GetAsync("/api/v1/artifacts?status=ARCHIVED"))
            {
                using var body = await employeeArchived.ReadJsonAsync();
                Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
            }
        }
        await environment.SeedUserAsync("boss", "SYSTEM_ADMIN");
        using (var admin = environment.CreateClient())
        {
            var adminHeaders = await admin.LoginAsync("boss");
            using (var adminArchived = await admin.GetAsync("/api/v1/artifacts?status=ARCHIVED"))
            {
                using var body = await adminArchived.ReadJsonAsync();
                Assert.Equal(1, body.RootElement.GetProperty("total").GetInt32());
                Assert.Equal(
                    "ARCHIVED",
                    body.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
            }
            using (var adminDefault = await admin.GetAsync("/api/v1/artifacts"))
            {
                using var body = await adminDefault.ReadJsonAsync();
                Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
            }
        }
        using (var edit = await client.PatchJsonAsync(
            $"/api/v1/artifacts/{artifactId}",
            new { title = "归档后不可编辑" },
            authorHeaders))
        {
            Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        }
        using (var restoreRequest = StateActionRequest(authorHeaders, $"/api/v1/artifacts/{artifactId}/restore"))
        using (var restored = await client.SendAsync(restoreRequest))
        {
            Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
            using var body = await restored.ReadJsonAsync();
            Assert.Equal("PUBLISHED", body.RootElement.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task FileOwnershipStateConflictsAndDelete()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("first");
        await environment.SeedUserAsync("second");
        using var client = environment.CreateClient();
        var firstHeaders = await client.LoginAsync("first");

        int unreferencedId;
        using (var upload = await client.SendAsync(UploadRequest(firstHeaders, "unused.txt", "unused", "text/plain")))
        {
            using var body = await upload.ReadJsonAsync();
            unreferencedId = body.RootElement.GetProperty("id").GetInt32();
        }
        using (var deleteRequest = DeleteRequest(firstHeaders, $"/api/v1/files/{unreferencedId}"))
        using (var deleted = await client.SendAsync(deleteRequest))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
        using (var download = await client.GetAsync($"/api/v1/files/{unreferencedId}/download"))
        {
            Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        }

        int ownedId;
        using (var upload = await client.SendAsync(UploadRequest(firstHeaders, "owned.txt", "owned", "text/plain")))
        {
            using var body = await upload.ReadJsonAsync();
            ownedId = body.RootElement.GetProperty("id").GetInt32();
        }

        using (var second = environment.CreateClient())
        {
            var secondHeaders = await second.LoginAsync("second");
            using var forbidden = await second.PostJsonAsync(
                "/api/v1/artifacts",
                new
                {
                    title = "跨用户附件",
                    summary = "不允许使用其他人的上传文件",
                    content_markdown = "正文",
                    file_ids = new List<int> { ownedId },
                },
                secondHeaders);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        int artifactId;
        using (var draft = await client.CreateDraftAsync(
            firstHeaders,
            new Dictionary<string, object?> { ["file_ids"] = new List<int> { ownedId } }))
        {
            artifactId = draft.RootElement.GetProperty("id").GetInt32();
        }
        using (var deleteRequest = DeleteRequest(firstHeaders, $"/api/v1/files/{ownedId}"))
        using (var inUse = await client.SendAsync(deleteRequest))
        {
            Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        }
        using (var publishRequest = StateActionRequest(firstHeaders, $"/api/v1/artifacts/{artifactId}/publish"))
        using (var published = await client.SendAsync(publishRequest))
        {
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        }
        using (var publishAgain = StateActionRequest(firstHeaders, $"/api/v1/artifacts/{artifactId}/publish"))
        using (var republished = await client.SendAsync(publishAgain))
        {
            Assert.Equal(HttpStatusCode.Conflict, republished.StatusCode);
        }
        using (var deleteArtifact = DeleteRequest(firstHeaders, $"/api/v1/artifacts/{artifactId}"))
        using (var notDraft = await client.SendAsync(deleteArtifact))
        {
            Assert.Equal(HttpStatusCode.Conflict, notDraft.StatusCode);
        }
    }

    [Fact]
    public async Task ExploreReturnsLatestSixPublishedArtifacts()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("author");
        using var client = environment.CreateClient();
        var headers = await client.LoginAsync("author");

        var artifactIds = new List<int>();
        for (var index = 0; index < 7; index += 1)
        {
            using var draft = await client.CreateDraftAsync(
                headers,
                new Dictionary<string, object?>
                {
                    ["title"] = $"展品 {index}",
                    ["summary"] = $"第 {index} 条展品",
                });
            var artifactId = draft.RootElement.GetProperty("id").GetInt32();
            artifactIds.Add(artifactId);
            using var publishRequest = StateActionRequest(headers, $"/api/v1/artifacts/{artifactId}/publish");
            using var published = await client.SendAsync(publishRequest);
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        }

        using var response = await client.GetAsync("/api/v1/explore");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.ReadJsonAsync();
        var latest = body.RootElement.GetProperty("latest_artifacts");
        Assert.Equal(6, latest.GetArrayLength());
        Assert.Equal(artifactIds[^1], latest[0].GetProperty("id").GetInt32());
        var latestIds = latest.EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).ToList();
        Assert.DoesNotContain(artifactIds[0], latestIds);
    }

    private static HttpRequestMessage UploadRequest(
        Dictionary<string, string> headers,
        string filename,
        string content,
        string contentType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files")
        {
            Content = BuildForm(filename, content, contentType),
        };
        foreach (var (key, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }
        return request;
    }

    private static MultipartFormDataContent BuildForm(
        string filename,
        string content,
        string contentType)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new StringContent(content, Encoding.UTF8);
        fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        form.Add(fileContent, "file", filename);
        return form;
    }

    private static HttpRequestMessage StateActionRequest(Dictionary<string, string> headers, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        foreach (var (key, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }
        return request;
    }

    private static HttpRequestMessage DeleteRequest(Dictionary<string, string> headers, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        foreach (var (key, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }
        return request;
    }
}
