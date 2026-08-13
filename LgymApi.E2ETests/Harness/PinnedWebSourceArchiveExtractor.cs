using System.Formats.Tar;

namespace LgymApi.E2ETests.Harness;

internal static class PinnedWebSourceArchiveExtractor
{
    internal const string ArchiveValidationMessage = "The pinned web source archive is unsafe or does not match its Git tree.";

    internal static async Task ExtractAsync(
        string archivePath,
        string destinationPath,
        GitTreeManifest manifest,
        GitObjectFormat objectFormat,
        CancellationToken cancellationToken = default)
    {
        var destinationCreated = false;
        try
        {
            ValidateDestination(destinationPath);
            await ValidateArchiveAsync(archivePath, destinationPath, manifest, objectFormat, cancellationToken);
            Directory.CreateDirectory(destinationPath);
            destinationCreated = true;
            EnsureNotReparsePoint(destinationPath);
            await ExtractArchiveAsync(archivePath, destinationPath, cancellationToken);
            await ValidateExtractedSetAsync(destinationPath, manifest, objectFormat, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDeletePartialDestination(destinationPath, destinationCreated);
            throw new InvalidOperationException(ArchiveValidationMessage);
        }
        catch
        {
            TryDeletePartialDestination(destinationPath, destinationCreated);
            throw;
        }
    }

    private static async Task ValidateArchiveAsync(
        string archivePath,
        string destinationPath,
        GitTreeManifest manifest,
        GitObjectFormat objectFormat,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seenDirectories = new HashSet<string>(StringComparer.Ordinal);
        var caseInsensitivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedDirectories = GetExpectedDirectories(manifest.Entries.Keys);
        var globalHeaderObserved = false;
        PinnedWebSourcePathPolicy.EnsureNoWindowsCollisions(manifest.Entries.Keys, ArchiveValidationMessage);
        await using var archive = File.OpenRead(archivePath);
        using var reader = new TarReader(archive, leaveOpen: false);
        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.EntryType == TarEntryType.GlobalExtendedAttributes &&
                !globalHeaderObserved &&
                seen.Count == 0 &&
                seenDirectories.Count == 0)
            {
                globalHeaderObserved = true;
                continue;
            }

            if (entry.EntryType == TarEntryType.Directory)
            {
                var directoryName = entry.Name.TrimEnd('/');
                if (!expectedDirectories.Contains(directoryName) ||
                    !seenDirectories.Add(directoryName) ||
                    !caseInsensitivePaths.Add(directoryName))
                {
                    throw new InvalidOperationException(ArchiveValidationMessage);
                }

                PinnedWebSourcePathPolicy.ResolveDestination(destinationPath, directoryName);
                continue;
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
                entry.DataStream is null ||
                !manifest.Entries.TryGetValue(entry.Name, out var expectedObjectId) ||
                !seen.Add(entry.Name) ||
                !caseInsensitivePaths.Add(entry.Name))
            {
                throw new InvalidOperationException(ArchiveValidationMessage);
            }

            PinnedWebSourcePathPolicy.ResolveDestination(destinationPath, entry.Name);
            var actualObjectId = await GitBlobHasher.ComputeObjectIdAsync(
                entry.DataStream,
                entry.Length,
                objectFormat,
                cancellationToken);
            if (!string.Equals(actualObjectId, expectedObjectId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(ArchiveValidationMessage);
            }
        }

        if (!seen.SetEquals(manifest.Entries.Keys))
        {
            throw new InvalidOperationException(ArchiveValidationMessage);
        }
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var archive = File.OpenRead(archivePath);
        using var reader = new TarReader(archive, leaveOpen: false);
        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken)) is not null)
        {
            if (entry.EntryType is TarEntryType.Directory or TarEntryType.GlobalExtendedAttributes)
            {
                continue;
            }

            var destinationFile = PinnedWebSourcePathPolicy.ResolveDestination(destinationPath, entry.Name);
            var parent = Path.GetDirectoryName(destinationFile)
                ?? throw new InvalidOperationException(ArchiveValidationMessage);
            Directory.CreateDirectory(parent);
            EnsureSafeDestinationChain(destinationPath, parent);
            await using var output = new FileStream(
                destinationFile,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await entry.DataStream!.CopyToAsync(output, cancellationToken);
        }
    }

    private static async Task ValidateExtractedSetAsync(
        string destinationPath,
        GitTreeManifest manifest,
        GitObjectFormat objectFormat,
        CancellationToken cancellationToken)
    {
        var extractedPaths = Directory.EnumerateFiles(destinationPath, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(destinationPath, path).Replace('\\', '/'),
                path => path,
                StringComparer.Ordinal);
        if (!extractedPaths.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(manifest.Entries.Keys))
        {
            throw new InvalidOperationException(ArchiveValidationMessage);
        }


        foreach (var (relativePath, path) in extractedPaths)
        {
            EnsureSafeDestinationChain(destinationPath, Path.GetDirectoryName(path)!);
            await using var stream = File.OpenRead(path);
            var objectId = await GitBlobHasher.ComputeObjectIdAsync(
                stream,
                stream.Length,
                objectFormat,
                cancellationToken);
            if (!string.Equals(objectId, manifest.Entries[relativePath], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(ArchiveValidationMessage);
            }
        }
    }

    private static HashSet<string> GetExpectedDirectories(IEnumerable<string> paths)
    {
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var separator = path.IndexOf('/');
            while (separator >= 0)
            {
                directories.Add(path[..separator]);
                separator = path.IndexOf('/', separator + 1);
            }
        }

        return directories;
    }

    private static void ValidateDestination(string destinationPath)
    {
        if (!Path.IsPathFullyQualified(destinationPath) || Directory.Exists(destinationPath) || File.Exists(destinationPath))
        {
            throw new InvalidOperationException(ArchiveValidationMessage);
        }

        var parent = Path.GetDirectoryName(destinationPath);
        if (parent is null || !Directory.Exists(parent))
        {
            throw new InvalidOperationException(ArchiveValidationMessage);
        }

        EnsureNotReparsePoint(parent);
    }

    private static void EnsureSafeDestinationChain(string root, string directory)
    {
        EnsureNotReparsePoint(root);
        var relative = Path.GetRelativePath(root, directory);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            EnsureNotReparsePoint(current);
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(ArchiveValidationMessage);
        }
    }

    private static void TryDeletePartialDestination(string destinationPath, bool destinationCreated)
    {
        try
        {
            if (!destinationCreated || !Directory.Exists(destinationPath))
            {
                return;
            }

            if ((File.GetAttributes(destinationPath) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(destinationPath);
                return;
            }

            Directory.Delete(destinationPath, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
