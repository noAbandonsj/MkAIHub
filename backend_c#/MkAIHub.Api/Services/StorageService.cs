using System.Security.Cryptography;
using MkAIHub.Api.Core;

namespace MkAIHub.Api.Services;

/// <summary>The metadata persisted for one accepted upload.</summary>
public sealed record SavedUpload(
    string OriginalName,
    string StoredName,
    string RelativePath,
    string Extension,
    string MimeType,
    int SizeBytes,
    string Sha256);

/// <summary>
/// Small local-file storage helper used by artifact attachments.
/// Mirrors app.services.storage.
/// </summary>
public static class StorageService
{
    private const int ChunkSize = 1024 * 1024;

    public static string UploadRoot(Settings settings)
        => Path.GetFullPath(settings.UploadDir);

    private static string DisplayFilename(string? filename)
    {
        var segments = (filename ?? string.Empty).Replace('\\', '/').Split('/');
        var cleaned = segments.Length > 0 ? segments[^1].Trim() : string.Empty;
        if (cleaned.Length == 0
            || cleaned.Length > 255
            || cleaned.Any(character => character < ' '))
        {
            throw new AppError("FILE_NAME_INVALID", "File name is invalid", 400);
        }
        return cleaned;
    }

    /// <summary>
    /// Persist one upload: stream to a temp file while hashing, enforce the
    /// size cap, then atomically move it under yyyy/MM/&lt;uuid&gt;.&lt;ext&gt;.
    /// </summary>
    public static async Task<SavedUpload> SaveUploadAsync(
        Stream fileStream,
        string? filename,
        string? contentType,
        Settings settings,
        CancellationToken cancellationToken = default)
    {
        var originalName = DisplayFilename(filename);
        var extension = Path.GetExtension(originalName).ToLowerInvariant().TrimStart('.');
        if (extension.Length == 0 || !settings.AllowedExtensions.Contains(extension))
        {
            throw new AppError("FILE_TYPE_NOT_ALLOWED", "This file type is not allowed", 415);
        }

        var now = DateTime.UtcNow;
        var storedName = $"{Guid.NewGuid():N}.{extension}";
        var relativePath = $"{now.Year:0000}/{now.Month:00}/{storedName}";
        var root = UploadRoot(settings);
        var destination = ResolveWithinRoot(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var tempDirectory = Path.Combine(root, ".tmp");
        Directory.CreateDirectory(tempDirectory);

        var maxBytes = (long)settings.MaxUploadSizeMb * 1024 * 1024;
        int sizeBytes = 0;
        using var digest = SHA256.Create();
        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var temporary = File.Create(tempPath))
            {
                var buffer = new byte[ChunkSize];
                int read;
                while ((read = await fileStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    sizeBytes += read;
                    if (sizeBytes > maxBytes)
                    {
                        throw new AppError(
                            "FILE_TOO_LARGE",
                            $"File exceeds the {settings.MaxUploadSizeMb} MB limit",
                            413);
                    }
                    digest.TransformBlock(buffer, 0, read, null, 0);
                    await temporary.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            if (sizeBytes == 0)
            {
                throw new AppError("FILE_EMPTY", "Empty files cannot be uploaded", 400);
            }
            digest.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            File.Move(tempPath, destination, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }

        var mimeType = string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType;
        if (mimeType.Length > 150)
        {
            mimeType = mimeType[..150];
        }
        return new SavedUpload(
            originalName,
            storedName,
            relativePath.Replace('\\', '/'),
            extension,
            mimeType,
            sizeBytes,
            Convert.ToHexString(digest.Hash!).ToLowerInvariant());
    }

    /// <summary>Resolve a stored relative path, rejecting escapes from the root.</summary>
    public static string StoredPath(string relativePath, Settings settings)
        => ResolveWithinRoot(UploadRoot(settings), relativePath);

    private static string ResolveWithinRoot(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new AppError("FILE_PATH_INVALID", "File storage path is invalid", 500);
        }
        return candidate;
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Multipart upload helpers built on the framework form reader.</summary>
public static class MultipartUpload
{
    /// <summary>
    /// Find the "file" form part. Returns null when the request has no
    /// multipart body or no file part (mapped to a validation error).
    /// </summary>
    public static async Task<IFormFile?> FindFileSectionAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            return form.Files.FirstOrDefault(file => file.Name == "file");
        }
        return null;
    }

    public static ApiValidationException MissingFileError()
        => new(new[]
        {
            new ValidationErrorDetail("missing", new object[] { "body", "file" }, "Field required"),
        });
}
