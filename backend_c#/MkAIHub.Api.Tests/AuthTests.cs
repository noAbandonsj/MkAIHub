using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MkAIHub.Api.Tests;

public sealed class AuthTests
{
    [Fact]
    public async Task LoginMeLogoutAndCookieContract()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("admin", "SYSTEM_ADMIN");
        using var client = environment.CreateClient();

        using var login = await client.PostJsonAsync(
            "/api/v1/auth/login",
            new { username = "ADMIN", password = "password1" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = await login.ReadJsonAsync();
        Assert.Equal("admin", body.RootElement.GetProperty("user").GetProperty("username").GetString());

        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);

        using (var me = await client.GetAsync("/api/v1/auth/me"))
        {
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        var token = await client.GetCsrfAsync();
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutRequest.Headers.TryAddWithoutValidation("X-CSRF-Token", token);
        using var logout = await client.SendAsync(logoutRequest);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var rejected = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Fact]
    public async Task WrongPasswordAndInactiveUserCannotLogin()
    {
        using var environment = new TestEnvironment();
        var userId = await environment.SeedUserAsync("admin", "SYSTEM_ADMIN");
        using var client = environment.CreateClient();

        using var wrong = await client.PostJsonAsync(
            "/api/v1/auth/login",
            new { username = "admin", password = "wrongpass" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        await using (var db = environment.CreateDbContext())
        {
            var user = await db.Users.FindAsync(userId);
            user!.IsActive = false;
            await db.SaveChangesAsync();
        }
        using var inactive = await client.PostJsonAsync(
            "/api/v1/auth/login",
            new { username = "admin", password = "password1" });
        Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
    }

    [Fact]
    public async Task CsrfAndValidationDoNotEchoPassword()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("admin", "SYSTEM_ADMIN");
        using var client = environment.CreateClient();
        await client.PostJsonAsync("/api/v1/auth/login", new { username = "admin", password = "password1" });

        using (var logout = await client.PostAsync("/api/v1/auth/logout", null))
        {
            Assert.Equal(HttpStatusCode.Forbidden, logout.StatusCode);
        }
        var token = await client.GetCsrfAsync();
        using var change = await client.PostJsonAsync(
            "/api/v1/auth/change-password",
            new { current_password = "short", new_password = "short" },
            new Dictionary<string, string> { ["X-CSRF-Token"] = token });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, change.StatusCode);
        var text = await change.Content.ReadAsStringAsync();
        Assert.DoesNotContain("short", text, StringComparison.Ordinal);
        Assert.DoesNotContain("input", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredAndRevokedSessionsAreRejected()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("admin", "SYSTEM_ADMIN");
        using var client = environment.CreateClient();
        await client.PostJsonAsync("/api/v1/auth/login", new { username = "admin", password = "password1" });

        using (var connection = environment.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE user_sessions SET expires_at = '2020-01-01 00:00:00'";
            command.ExecuteNonQuery();
        }
        using var response = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePasswordRevokesAllSessions()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("admin", "SYSTEM_ADMIN");
        using var client = environment.CreateClient();
        using var other = environment.CreateClient();

        await client.PostJsonAsync("/api/v1/auth/login", new { username = "admin", password = "password1" });
        await other.PostJsonAsync("/api/v1/auth/login", new { username = "admin", password = "password1" });

        var token = await client.GetCsrfAsync();
        using var change = await client.PostJsonAsync(
            "/api/v1/auth/change-password",
            new { current_password = "password1", new_password = "password2" },
            new Dictionary<string, string> { ["X-CSRF-Token"] = token });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        using (var me = await client.GetAsync("/api/v1/auth/me"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }
        using (var otherMe = await other.GetAsync("/api/v1/auth/me"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, otherMe.StatusCode);
        }
        using var relogin = await other.PostJsonAsync(
            "/api/v1/auth/login",
            new { username = "admin", password = "password2" });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    [Fact]
    public async Task AdminCrudAndEmployeeForbidden()
    {
        using var environment = new TestEnvironment();
        await environment.SeedUserAsync("admin", "SYSTEM_ADMIN");
        var employeeId = await environment.SeedUserAsync("employee", "EMPLOYEE");
        using var client = environment.CreateClient();
        await client.PostJsonAsync("/api/v1/auth/login", new { username = "admin", password = "password1" });
        var headers = new Dictionary<string, string> { ["X-CSRF-Token"] = await client.GetCsrfAsync() };

        using (var employeeClient = environment.CreateClient())
        {
            await employeeClient.PostJsonAsync("/api/v1/auth/login", new { username = "employee", password = "password1" });
            using var forbidden = await employeeClient.GetAsync("/api/v1/admin/users");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using (var created = await client.PostJsonAsync(
            "/api/v1/admin/users",
            new
            {
                username = "New.User",
                display_name = "New User",
                password = "password2",
                role = "EMPLOYEE",
            },
            headers))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var body = await created.ReadJsonAsync();
            Assert.Equal("new.user", body.RootElement.GetProperty("username").GetString());
        }

        using (var patched = await client.PatchJsonAsync(
            $"/api/v1/admin/users/{employeeId}",
            new { is_active = false },
            headers))
        {
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
            using var body = await patched.ReadJsonAsync();
            Assert.False(body.RootElement.GetProperty("is_active").GetBoolean());
        }
        using var reset = await client.PostJsonAsync(
            $"/api/v1/admin/users/{employeeId}/reset-password",
            new { new_password = "password3" },
            headers);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }

    [Fact]
    public async Task AdminCannotDeactivateOrDowngradeSelf()
    {
        using var environment = new TestEnvironment();
        var adminId = await environment.SeedUserAsync("admin", "SYSTEM_ADMIN");
        using var client = environment.CreateClient();
        await client.PostJsonAsync("/api/v1/auth/login", new { username = "admin", password = "password1" });
        var headers = new Dictionary<string, string> { ["X-CSRF-Token"] = await client.GetCsrfAsync() };

        using var deactivate = await client.PatchJsonAsync(
            $"/api/v1/admin/users/{adminId}",
            new { is_active = false },
            headers);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        using var downgrade = await client.PatchJsonAsync(
            $"/api/v1/admin/users/{adminId}",
            new { role = "EMPLOYEE" },
            headers);
        Assert.Equal(HttpStatusCode.Forbidden, downgrade.StatusCode);
    }
}
