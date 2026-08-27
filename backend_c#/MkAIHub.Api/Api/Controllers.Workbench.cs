using Microsoft.AspNetCore.Mvc;
using MkAIHub.Api.Data;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>Personal workbench and closure statistics endpoint.</summary>
[Route("api/v1/workbench")]
public sealed class WorkbenchController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;

    public WorkbenchController(AppDbContext db, Authenticator auth)
    {
        _db = db;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> GetWorkbench(CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        return this.SnakeJson(await WorkbenchService.BuildResponseAsync(_db, auth.User));
    }
}
