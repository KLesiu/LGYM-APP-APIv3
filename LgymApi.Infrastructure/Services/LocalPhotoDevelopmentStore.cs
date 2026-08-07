using System.Security.Cryptography;
using Microsoft.AspNetCore.StaticFiles;

namespace LgymApi.Infrastructure.Services;

public sealed class LocalPhotoDevelopmentStore
{
    private readonly string _rootPath;
    private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

    public LocalPhotoDevelopmentStore()
    {
        _rootPath = Path.Combine(AppContext.BaseDirectory, "dev-photo-storage");
    }

    public string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key is required.", nameof(storageKey));
        }

        var normalizedSegments = storageKey
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .ToArray();

        if (normalizedSegments.Length == 0 || normalizedSegments.Any(segment => segment == "." || segment == ".."))
        {
            throw new InvalidOperationException("Invalid storage key path.");
        }

        return Path.Combine(new[] { _rootPath }.Concat(normalizedSegments).ToArray());
    }

    public async Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
    {
        await SaveAsync(storageKey, content, long.MaxValue, cancellationToken);
    }

    public async Task SaveAsync(
        string storageKey,
        Stream content,
        long maxFileSizeBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileSizeBytes);
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePath(storageKey);
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = Path.Combine(
            directory!,
            $".{Path.GetFileName(path)}.{RandomNumberGenerator.GetHexString(16)}.tmp");
        try
        {
            await using (var fileStream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81_920];
                long totalBytes = 0;
                while (true)
                {
                    var bytesRead = await content.ReadAsync(buffer, cancellationToken);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    if (totalBytes > maxFileSizeBytes - bytesRead)
                    {
                        throw new InvalidDataException("Photo exceeds the configured maximum size.");
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalBytes += bytesRead;
                }

                await fileStream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<byte[]?> ReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(storageKey);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePath(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<LgymApi.Application.Abstractions.Storage.PhotoMetadata?> GetMetadataAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolvePath(storageKey);
        if (!File.Exists(path))
        {
            return Task.FromResult<LgymApi.Application.Abstractions.Storage.PhotoMetadata?>(null);
        }

        var fileInfo = new FileInfo(path);
        _contentTypeProvider.TryGetContentType(path, out var contentType);

        return Task.FromResult<LgymApi.Application.Abstractions.Storage.PhotoMetadata?>(new LgymApi.Application.Abstractions.Storage.PhotoMetadata
        {
            SizeBytes = fileInfo.Length,
            ContentType = contentType ?? "application/octet-stream",
            UploadedAt = fileInfo.CreationTimeUtc,
            ETag = fileInfo.LastWriteTimeUtc.Ticks.ToString()
        });
    }

    public string ResolveContentType(string storageKey)
    {
        var path = ResolvePath(storageKey);
        return _contentTypeProvider.TryGetContentType(path, out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}
