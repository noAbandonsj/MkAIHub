using Microsoft.AspNetCore.Mvc;
using MkAIHub.Api.Core;

namespace MkAIHub.Api.Api;

/// <summary>Unauthenticated service health endpoint.</summary>
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly Settings _settings;

    public HealthController(Settings settings)
    {
        _settings = settings;
    }

    [HttpGet]
    public IActionResult Health()
        => this.SnakeJson(new HealthResponseDto(
            "ok",
            _settings.AppName,
            _settings.AppEnv,
            AppVersion.Current));
}

public static class AppVersion
{
    public const string Current = "0.1.0";
}
