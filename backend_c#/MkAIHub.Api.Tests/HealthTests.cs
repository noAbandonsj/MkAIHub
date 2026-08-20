using System.Net;
using System.Text.Json;

namespace MkAIHub.Api.Tests;

public sealed class HealthTests
{
    [Fact]
    public async Task HealthEndpointReturnsServiceMetadata()
    {
        using var environment = new TestEnvironment();
        using var client = environment.CreateClient();

        using var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.ReadJsonAsync();
        var expected = JsonDocument.Parse(
            "{\"status\": \"ok\", \"service\": \"MkAIHub Test\", \"environment\": \"test\", \"version\": \"0.1.0\"}");
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(expected.RootElement),
            System.Text.Json.JsonSerializer.Serialize(body.RootElement));
    }

    [Fact]
    public async Task UnknownApiPathUsesUniformErrorResponse()
    {
        using var environment = new TestEnvironment();
        using var client = environment.CreateClient();

        using var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = await response.ReadJsonAsync();
        Assert.Equal("NOT_FOUND", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("API route not found", body.RootElement.GetProperty("message").GetString());
    }
}
