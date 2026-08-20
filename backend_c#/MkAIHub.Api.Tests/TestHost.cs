using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MkAIHub.Api;
using MkAIHub.Api.Data;
using MkAIHub.Api.Security;

namespace MkAIHub.Api.Tests;

/// <summary>
/// Isolated test environment: temporary SQLite database (schema applied by the
/// migrator), temporary upload directory, and cookie-isolated HTTP clients.
/// Mirrors the Python conftest fixtures.
/// </summary>
public sealed class TestEnvironment : IDisposable
{
    public string Root { get; }

    public string DatabasePath { get; }

    public string UploadDir { get; }

    public ApiFactory Factory { get; }

    public TestEnvironment()
    {
        Root = Path.Combine(Path.GetTempPath(), "mkaihub-csharp-tests", Guid.NewGuid().ToString("N"));
        DatabasePath = Path.Combine(Root, "mkaihub-test.db");
        UploadDir = Path.Combine(Root, "uploads");
        Directory.CreateDirectory(Root);
        Migrator.UpgradeToHead(DatabasePath);
        Factory = new ApiFactory(DatabasePath, UploadDir, Path.Combine(Root, "data"));
    }

    public HttpClient CreateClient()
    {
        var server = Factory.Server;
        return new HttpClient(new CookieRecordingHandler(server.CreateHandler()))
        {
            BaseAddress = server.BaseAddress,
        };
    }

    public string SqliteUrl => $"sqlite:///{DatabasePath.Replace('\\', '/')}";

    public async Task<int> SeedUserAsync(string username, string role = "EMPLOYEE")
    {
        await using var db = CreateDbContext();
        var now = DateTime.UtcNow;
        var user = new User
        {
            Username = username,
            DisplayName = char.ToUpperInvariant(username[0]) + username[1..],
            PasswordHash = PasswordHasher.HashPassword("password1"),
            Role = role,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(Database.ConnectionStringFor(DatabasePath))
            .Options;
        return new AppDbContext(options);
    }

    public SqliteConnection OpenConnection() => Database.OpenSqlite(DatabasePath);

    public void Dispose() => Factory.Dispose();
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath;
    private readonly string _uploadDir;
    private readonly string _dataDir;

    public ApiFactory(string databasePath, string uploadDir, string dataDir)
    {
        _databasePath = databasePath;
        _uploadDir = uploadDir;
        _dataDir = dataDir;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("test");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["APP_ENV"] = "test",
                ["APP_NAME"] = "MkAIHub Test",
                ["DATABASE_URL"] = $"sqlite:///{_databasePath.Replace('\\', '/')}",
                ["UPLOAD_DIR"] = _uploadDir,
                ["DATA_DIR"] = _dataDir,
                ["APP_SECRET_KEY"] = "test-secret-key-for-csrf-32-bytes-minimum",
                ["LOG_LEVEL"] = "ERROR",
                ["FRONTEND_ORIGIN"] = "",
            });
        });
    }
}

/// <summary>
/// Minimal cookie jar so each HttpClient keeps its own session cookie,
/// mirroring separate Python TestClient instances.
/// </summary>
public sealed class CookieRecordingHandler : DelegatingHandler
{
    private readonly Dictionary<string, string> _cookies = new();

    public CookieRecordingHandler(HttpMessageHandler inner)
        : base(inner)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_cookies.Count > 0)
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie",
                string.Join("; ", _cookies.Select(pair => $"{pair.Key}={pair.Value}")));
        }
        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var header in setCookies)
            {
                var firstPair = header.Split(';', 2)[0].Trim();
                var separator = firstPair.IndexOf('=');
                if (separator > 0)
                {
                    _cookies[firstPair[..separator]] = firstPair[(separator + 1)..];
                }
            }
        }
        return response;
    }
}

public static class TestHttp
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<JsonDocument> ReadJsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    public static async Task<string> GetCsrfAsync(this HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/auth/csrf-token");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var body = await response.ReadJsonAsync();
        return body.RootElement.GetProperty("csrf_token").GetString()!;
    }

    public static async Task<Dictionary<string, string>> LoginAsync(this HttpClient client, string username)
    {
        using var response = await client.PostAsync(
            "/api/v1/auth/login",
            new StringContent(
                JsonSerializer.Serialize(new { username, password = "password1" }),
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var token = await client.GetCsrfAsync();
        return new Dictionary<string, string> { ["X-CSRF-Token"] = token };
    }

    public static async Task<HttpResponseMessage> PostJsonAsync(
        this HttpClient client,
        string url,
        object payload,
        Dictionary<string, string>? headers = null)
        => await client.SendWithHeadersAsync(HttpMethod.Post, url, payload, headers);

    public static async Task<HttpResponseMessage> PatchJsonAsync(
        this HttpClient client,
        string url,
        object payload,
        Dictionary<string, string>? headers = null)
        => await client.SendWithHeadersAsync(new HttpMethod("PATCH"), url, payload, headers);

    private static async Task<HttpResponseMessage> SendWithHeadersAsync(
        this HttpClient client,
        HttpMethod method,
        string url,
        object payload,
        Dictionary<string, string>? headers)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
        };
        foreach (var (key, value) in headers ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }
        return await client.SendAsync(request);
    }

    public static async Task<JsonDocument> CreateDraftAsync(
        this HttpClient client,
        Dictionary<string, string> headers,
        object? overrides = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "团队提示词实践",
            ["summary"] = "一条可复用的内部 AI 实践摘要",
            ["content_markdown"] = "# 使用方式\n\n按步骤执行。",
            ["file_ids"] = new List<int>(),
        };
        if (overrides is Dictionary<string, object?> additional)
        {
            foreach (var (key, value) in additional)
            {
                payload[key] = value;
            }
        }
        using var response = await client.PostJsonAsync("/api/v1/artifacts", payload, headers);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Created,
            $"create failed: {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text);
    }

    public static async Task<JsonDocument> UploadFileAsync(
        this HttpClient client,
        Dictionary<string, string> headers,
        string filename,
        byte[] content,
        string contentType)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(fileContent, "file", filename);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/files") { Content = form };
        foreach (var (key, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == System.Net.HttpStatusCode.Created,
            $"upload failed: {(int)response.StatusCode} {text}");
        return JsonDocument.Parse(text);
    }
}
