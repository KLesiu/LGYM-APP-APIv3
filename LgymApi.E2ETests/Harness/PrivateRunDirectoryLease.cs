using System.Security.Cryptography;

namespace LgymApi.E2ETests.Harness;

internal sealed record PrivateRunDirectoryRequest(string RepositoryRoot, string PrivateRunRoot, TimeSpan CleanupTimeout);

internal interface IRunDirectoryCleaner
{
    Task DeleteAsync(string runDirectory, CancellationToken cancellationToken);
}

internal sealed class PrivateRunDirectoryLease : IAsyncDisposable
{
    internal const string PathValidationMessage = "E2E private runtime path validation failed.";
    internal const string CleanupMessage = "E2E private runtime cleanup failed.";

    private readonly PrivateRunDirectoryLayout _layout;
    private readonly IRunDirectoryCleaner _cleaner;
    private int _cleaned;

    private PrivateRunDirectoryLease(PrivateRunDirectoryLayout layout, string runDirectory, IRunDirectoryCleaner cleaner)
    {
        _layout = layout;
        RunDirectory = runDirectory;
        _cleaner = cleaner;
    }

    internal string RunDirectory { get; }

    internal void EnsureSafeRuntimeArtifact(string artifactPath)
    {
        var apiDirectory = Path.Combine(RunDirectory, "api");
        if (!PrivateRunDirectoryLayout.IsDescendantOrSame(apiDirectory, artifactPath))
        {
            throw new InvalidOperationException(PathValidationMessage);
        }

        _layout.EnsureOwnedRunDirectory(RunDirectory);
        _layout.EnsureSafePath(apiDirectory);
        _layout.EnsureSafePath(artifactPath);
    }

    internal static PrivateRunDirectoryLease Create(
        PrivateRunDirectoryRequest request,
        IRunDirectoryCleaner? cleaner = null)
    {
        var layout = PrivateRunDirectoryLayout.Resolve(request);
        layout.CreateRunRoot();
        var runDirectory = Path.Combine(
            layout.RunRoot,
            RandomNumberGenerator.GetHexString(32, lowercase: true));

        if (Directory.Exists(runDirectory))
        {
            throw new InvalidOperationException(PathValidationMessage);
        }

        Directory.CreateDirectory(runDirectory);
        layout.EnsureSafePath(runDirectory);
        return new PrivateRunDirectoryLease(layout, runDirectory, cleaner ?? new FileSystemRunDirectoryCleaner());
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _cleaned) != 0)
        {
            return;
        }

        try
        {
            _layout.EnsureOwnedRunDirectory(RunDirectory);
            using var timeout = new CancellationTokenSource(CleanupTimeout);
            await _cleaner.DeleteAsync(RunDirectory, timeout.Token);
            Interlocked.Exchange(ref _cleaned, 1);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            throw new InvalidOperationException(CleanupMessage);
        }
    }

    private TimeSpan CleanupTimeout => _layout.CleanupTimeout;
}

internal sealed class PrivateRunDirectoryLayout
{
    private PrivateRunDirectoryLayout(string repositoryRoot, string privateRoot, string runRoot, TimeSpan cleanupTimeout)
    {
        RepositoryRoot = repositoryRoot;
        PrivateRoot = privateRoot;
        RunRoot = runRoot;
        CleanupTimeout = cleanupTimeout;
    }

    private string RepositoryRoot { get; }

    private string PrivateRoot { get; }

    internal string RunRoot { get; }

    internal TimeSpan CleanupTimeout { get; }

    internal static PrivateRunDirectoryLayout Resolve(PrivateRunDirectoryRequest request)
    {
        try
        {
            if (request.CleanupTimeout <= TimeSpan.Zero || string.IsNullOrWhiteSpace(request.RepositoryRoot) ||
                string.IsNullOrWhiteSpace(request.PrivateRunRoot) ||
                Path.IsPathRooted(request.PrivateRunRoot) ||
                Path.IsPathFullyQualified(request.PrivateRunRoot))
            {
                throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage);
            }

            if (!string.Equals(request.PrivateRunRoot.Replace('\\', '/'), ".e2e-private/runs", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage);
            }

            var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.RepositoryRoot));
            var privateRoot = Path.Combine(repositoryRoot, ".e2e-private");
            var runRoot = Path.GetFullPath(Path.Combine(repositoryRoot, request.PrivateRunRoot));
            if (!IsStrictDescendant(privateRoot, runRoot))
            {
                throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage);
            }

            var layout = new PrivateRunDirectoryLayout(repositoryRoot, privateRoot, runRoot, request.CleanupTimeout);
            layout.EnsureSafePath(runRoot);
            return layout;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage);
        }
    }

    internal void CreateRunRoot()
    {
        Directory.CreateDirectory(PrivateRoot);
        EnsureSafePath(PrivateRoot);
        Directory.CreateDirectory(RunRoot);
        EnsureSafePath(RunRoot);
    }

    internal void EnsureOwnedRunDirectory(string runDirectory)
    {
        if (!IsStrictDescendant(RunRoot, runDirectory))
        {
            throw new InvalidOperationException(PrivateRunDirectoryLease.CleanupMessage);
        }

        EnsureSafePath(runDirectory);
    }

    internal void EnsureSafePath(string candidatePath)
    {
        if (!IsDescendantOrSame(RepositoryRoot, candidatePath))
        {
            throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage);
        }

        var relativePath = Path.GetRelativePath(RepositoryRoot, candidatePath);
        var currentPath = RepositoryRoot;
        EnsureNotReparsePoint(currentPath);
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            EnsureNotReparsePoint(currentPath);
        }
    }

    private static bool IsStrictDescendant(string parentPath, string candidatePath) =>
        IsDescendantOrSame(parentPath, candidatePath) &&
        !string.Equals(Path.GetFullPath(parentPath), Path.GetFullPath(candidatePath), StringComparison.OrdinalIgnoreCase);

    internal static bool IsDescendantOrSame(string parentPath, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(parentPath, candidatePath);
        return !Path.IsPathRooted(relativePath) &&
               relativePath != ".." &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((Directory.Exists(path) || File.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage);
        }
    }
}

internal sealed class FileSystemRunDirectoryCleaner : IRunDirectoryCleaner
{
    public Task DeleteAsync(string runDirectory, CancellationToken cancellationToken) =>
        DeleteDirectoryAsync(runDirectory, cancellationToken);

    private static async Task DeleteDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0 &&
                (attributes & FileAttributes.ReparsePoint) == 0)
            {
                await DeleteDirectoryAsync(entry, cancellationToken);
            }
            else if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.Delete(entry);
            }
            else
            {
                File.Delete(entry);
            }
            await Task.Yield();
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(directory);
    }
}
