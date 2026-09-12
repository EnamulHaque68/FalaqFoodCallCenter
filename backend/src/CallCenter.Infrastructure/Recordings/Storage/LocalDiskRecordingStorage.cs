using System.Security.Cryptography;
using CallCenter.Application.Recordings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CallCenter.Infrastructure.Recordings.Storage;

public sealed class LocalDiskRecordingStorage : IRecordingStorage
{
    private readonly string _baseStoragePath;

    public LocalDiskRecordingStorage(
        IOptions<RecordingStorageOptions> options,
        IHostEnvironment hostEnvironment)
    {
        var rawPath = options.Value.StoragePath;
        if (Path.IsPathRooted(rawPath))
        {
            _baseStoragePath = Path.GetFullPath(rawPath);
        }
        else
        {
            _baseStoragePath = Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, rawPath));
        }

        Directory.CreateDirectory(_baseStoragePath);
    }

    public string BaseStoragePath => _baseStoragePath;

    public async Task<string> SaveAsync(
        Stream content,
        string relativeKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var safeFullPath = ResolveSafePath(relativeKey);
        var parentDir = Path.GetDirectoryName(safeFullPath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        await using var fileStream = new FileStream(
            safeFullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(cancellationToken);

        // Normalize relative key for storage
        return Path.GetRelativePath(_baseStoragePath, safeFullPath).Replace('\\', '/');
    }

    public Task<Stream?> GetStreamAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        var safeFullPath = ResolveSafePath(storageKey);
        if (!File.Exists(safeFullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            safeFullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        var safeFullPath = ResolveSafePath(storageKey);
        if (!File.Exists(safeFullPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(safeFullPath);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        var safeFullPath = ResolveSafePath(storageKey);
        return Task.FromResult(File.Exists(safeFullPath));
    }

    public Task<long?> GetFileSizeAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        var safeFullPath = ResolveSafePath(storageKey);
        if (!File.Exists(safeFullPath))
        {
            return Task.FromResult<long?>(null);
        }

        var info = new FileInfo(safeFullPath);
        return Task.FromResult<long?>(info.Length);
    }

    public async Task<string> ComputeSha256Async(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        var safeFullPath = ResolveSafePath(storageKey);
        if (!File.Exists(safeFullPath))
        {
            return string.Empty;
        }

        await using var stream = File.OpenRead(safeFullPath);
        using var sha = SHA256.Create();
        var hashBytes = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private string ResolveSafePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Storage key cannot be null or empty.", nameof(key));
        }

        // Clean any leading slashes
        var normalized = key.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(_baseStoragePath, normalized));

        // Prevent directory traversal attacks
        if (!combined.StartsWith(_baseStoragePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Path traversal detected in recording storage key.");
        }

        return combined;
    }
}
