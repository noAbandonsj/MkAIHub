using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using MkAIHub.Api.Core;
using MkAIHub.Api.Data;
using MkAIHub.Api.Services;

namespace MkAIHub.Api.Api;

/// <summary>Authenticated local attachment upload and download endpoints.</summary>
[Route("api/v1/files")]
public sealed class FilesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Settings _settings;
    private readonly Authenticator _auth;

    public FilesController(AppDbContext db, Settings settings, Authenticator auth)
    {
        _db = db;
        _settings = settings;
        _auth = auth;
    }

    /// <summary>Store one multipart upload and register its metadata.</summary>
    [HttpPost]
    public async Task<IActionResult> UploadFile(CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var upload = await MultipartUpload.FindFileSectionAsync(Request, cancellationToken);
        if (upload is null)
        {
            throw MultipartUpload.MissingFileError();
        }
        var saved = await StorageService.SaveUploadAsync(
            upload.OpenReadStream(),
            upload.FileName,
            upload.ContentType,
            _settings,
            cancellationToken);
        var storedFile = new StoredFile
        {
            OriginalName = saved.OriginalName,
            StoredName = saved.StoredName,
            RelativePath = saved.RelativePath,
            Extension = saved.Extension,
            MimeType = saved.MimeType,
            SizeBytes = saved.SizeBytes,
            Sha256 = saved.Sha256,
            UploaderId = auth.User.Id,
            Uploader = auth.User,
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Files.Add(storedFile);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception)
        {
            StorageService.TryDelete(StorageService.StoredPath(saved.RelativePath, _settings));
            throw;
        }
        return this.SnakeJson(ArtifactService.ToFileRead(storedFile), statusCode: 201);
    }

    /// <summary>Download a stored file the caller is allowed to read.</summary>
    [HttpGet("{fileId}/download")]
    public async Task<IActionResult> DownloadFile(string fileId, CancellationToken cancellationToken)
    {
        var auth = await _auth.GetCurrentAuthAsync(Request);
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "file_id", "fileId");
        QueryParams.ThrowIfErrors(errors);

        var storedFile = await _db.Files.FindAsync(new object[] { parsedId }, cancellationToken);
        if (storedFile is null || storedFile.IsDeleted)
        {
            throw new AppError("FILE_NOT_FOUND", "File not found", 404);
        }

        var canDownload = storedFile.UploaderId == auth.User.Id
            || auth.User.Role == UserRoles.SystemAdmin;
        if (!canDownload)
        {
            canDownload = await _db.ArtifactFiles
                .Where(link => link.FileId == storedFile.Id && link.Artifact!.Status == ArtifactStatuses.Published)
                .AnyAsync(cancellationToken);
        }
        if (!canDownload)
        {
            throw new AppError("FILE_NOT_FOUND", "File not found", 404);
        }

        var path = StorageService.StoredPath(storedFile.RelativePath, _settings);
        if (!System.IO.File.Exists(path))
        {
            throw new AppError("FILE_CONTENT_MISSING", "Stored file is missing", 404);
        }
        Response.Headers[HeaderNames.ContentDisposition] =
            $"attachment; filename=\"{storedFile.OriginalName.Replace("\"", "%22")}\"";
        return PhysicalFile(path, storedFile.MimeType);
    }

    /// <summary>Soft-delete an unreferenced upload and unlink its content.</summary>
    [HttpDelete("{fileId}")]
    public async Task<IActionResult> DeleteFile(string fileId, CancellationToken cancellationToken)
    {
        var auth = _auth.RequireCsrf(Request, await _auth.GetCurrentAuthAsync(Request));
        var errors = new List<ValidationErrorDetail>();
        var parsedId = QueryParams.ParsePathInt(RouteData.Values, errors, "file_id", "fileId");
        QueryParams.ThrowIfErrors(errors);

        var storedFile = await _db.Files.FindAsync(new object[] { parsedId }, cancellationToken);
        if (storedFile is null || storedFile.IsDeleted)
        {
            throw new AppError("FILE_NOT_FOUND", "File not found", 404);
        }
        if (storedFile.UploaderId != auth.User.Id)
        {
            throw new AppError("FORBIDDEN", "Only the uploader can delete this file", 403);
        }
        var referenceCount = await _db.ArtifactFiles
            .CountAsync(link => link.FileId == storedFile.Id, cancellationToken);
        if (referenceCount > 0)
        {
            throw new AppError("FILE_IN_USE", "A referenced file cannot be deleted", 409);
        }
        storedFile.IsDeleted = true;
        await _db.SaveChangesAsync(cancellationToken);
        StorageService.TryDelete(StorageService.StoredPath(storedFile.RelativePath, _settings));
        return NoContent();
    }
}

/// <summary>Minimal explore aggregation endpoint.</summary>
[Route("api/v1/explore")]
public sealed class ExploreController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Authenticator _auth;

    public ExploreController(AppDbContext db, Authenticator auth)
    {
        _db = db;
        _auth = auth;
    }

    [HttpGet]
    public async Task<IActionResult> Explore(CancellationToken cancellationToken)
    {
        await _auth.GetCurrentAuthAsync(Request);
        var artifacts = await _db.Artifacts
            .WithDetails()
            .Where(artifact => artifact.Status == ArtifactStatuses.Published)
            .OrderByDescending(artifact => artifact.PublishedAt)
            .ThenByDescending(artifact => artifact.Id)
            .Take(6)
            .ToListAsync(cancellationToken);
        return this.SnakeJson(new ExploreResponseDto(
            artifacts.Select(ArtifactService.ToListItem).ToList()));
    }
}

/// <summary>Uniform 404 for every unmatched /api route and method.</summary>
[Route("api/{**path}")]
public sealed class ApiFallbackController : ControllerBase
{
    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD")]
    public IActionResult UnknownApiRoute()
    {
        throw new AppError("NOT_FOUND", "API route not found", 404);
    }
}
