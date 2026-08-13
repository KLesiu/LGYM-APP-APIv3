using System.Security.Cryptography;

namespace LgymApi.E2ETests.Harness;

internal sealed record PrivateRunDirectoryRequest(string RepositoryRoot, string PrivateRunRoot, TimeSpan CleanupTimeout);

internal enum PrivateRunCleanupStage
{
    Unknown,
    CachePathValidation,
    CacheDelete,
    RunValidation,
    Validation,
    Enumeration,
    Attributes,
    EntryDelete,
    ParentDelete
}

internal sealed class PrivateRunCleanupException(PrivateRunCleanupStage stage) : InvalidOperationException
{
    internal PrivateRunCleanupStage Stage { get; } = stage;
}

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
    private static readonly TimeSpan SharingViolationRetryDelay = TimeSpan.FromMilliseconds(100);
    private const int SharingViolationHResult = unchecked((int)0x80070020);
    private int _cleaned;

    private PrivateRunDirectoryLease(PrivateRunDirectoryLayout layout, string runDirectory, IRunDirectoryCleaner cleaner)
    {
        _layout = layout;
        RunDirectory = runDirectory;
        _cleaner = cleaner;
    }

    internal string RunDirectory { get; }

    internal PrivateRunCleanupStage CleanupStage { get; private set; }

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

    internal string ResolveWebOwnedPath(string relativeRoot) =>
        _layout.ResolveWebOwnedPath(RunDirectory, relativeRoot);

    internal string ResolveCacheOwnedPath(string relativeRoot) =>
        _layout.ResolveCacheOwnedPath(relativeRoot);

    internal string CreateLifecycleScenarioDirectory(string caseId) =>
        CreateLifecycleDirectory("scenarios", caseId);

    internal string CreateLifecycleArtifactDirectory(string caseId) =>
        CreateLifecycleDirectory("artifacts", caseId);

    internal string CreateLifecycleComponentDirectory(string caseId, string componentName)
    {
        EnsureCanonicalLifecycleId(caseId);
        if (componentName is not ("api" or "web-runtime" or "browser-runtime"))
        {
            throw new InvalidOperationException(PathValidationMessage);
        }

        var scenarioDirectory = CreateLifecycleScenarioDirectory(caseId);
        var componentDirectory = Path.Combine(scenarioDirectory, componentName);
        return CreateOwnedDirectory(componentDirectory);
    }

    internal Task DeleteLifecycleScenarioAsync(string caseId, CancellationToken cancellationToken = default) =>
        DeleteLifecycleDirectoryAsync("scenarios", caseId, cancellationToken);

    internal Task DeleteLifecycleComponentAsync(
        string caseId,
        string componentName,
        CancellationToken cancellationToken = default)
    {
        EnsureCanonicalLifecycleId(caseId);
        if (componentName is not ("api" or "web-runtime" or "browser-runtime"))
        {
            throw new InvalidOperationException(PathValidationMessage);
        }

        return DeleteOwnedDirectoryAsync(
            Path.Combine(RunDirectory, "scenarios", caseId, componentName),
            cancellationToken);
    }

    internal async Task FinalizeLifecycleFailureAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CleanupTimeout);
        try
        {
            ValidateOwnedRunDirectory();
            var artifactDirectory = Path.Combine(RunDirectory, "artifacts");
            _layout.EnsureSafePath(artifactDirectory);

            foreach (var entry in Directory.GetFileSystemEntries(RunDirectory))
            {
                _layout.EnsureSafePath(entry);
                if (string.Equals(Path.GetFileName(entry), "artifacts", StringComparison.Ordinal))
                {
                    continue;
                }

                await _cleaner.DeleteAsync(entry, timeout.Token);
            }

            ValidateOwnedRunDirectory();
            _layout.EnsureSafePath(artifactDirectory);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new InvalidOperationException(CleanupMessage);
        }
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
            ValidateOwnedRunDirectory();
            using var timeout = new CancellationTokenSource(CleanupTimeout);
            while (true)
            {
                try
                {
                    await _cleaner.DeleteAsync(RunDirectory, timeout.Token);
                    break;
                }
                catch (IOException exception) when (exception.HResult == SharingViolationHResult)
                {
                    await Task.Delay(SharingViolationRetryDelay, timeout.Token);
                    try
                    {
                        ValidateOwnedRunDirectory();
                    }
                    catch (PrivateRunCleanupException)
                    {
                        throw;
                    }
                    catch (Exception validationException) when (validationException is IOException or UnauthorizedAccessException)
                    {
                        throw new PrivateRunCleanupException(PrivateRunCleanupStage.RunValidation);
                    }
                }
            }
            Interlocked.Exchange(ref _cleaned, 1);
        }
        catch (PrivateRunCleanupException exception)
        {
            CleanupStage = exception.Stage;
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            CleanupStage = PrivateRunCleanupStage.Unknown;
            throw new InvalidOperationException(CleanupMessage);
        }
    }

    private TimeSpan CleanupTimeout => _layout.CleanupTimeout;

    internal static void EnsureCanonicalLifecycleId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
            !System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z0-9][a-z0-9-]{0,63}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(PathValidationMessage);
        }
    }

    private string CreateLifecycleDirectory(string rootDirectory, string caseId)
    {
        EnsureCanonicalLifecycleId(caseId);
        return CreateOwnedDirectory(Path.Combine(RunDirectory, rootDirectory, caseId));
    }

    private string CreateOwnedDirectory(string directory)
    {
        _layout.EnsureOwnedRunDirectory(RunDirectory);
        _layout.EnsureSafePath(directory);
        Directory.CreateDirectory(directory);
        _layout.EnsureSafePath(directory);
        return directory;
    }

    private Task DeleteLifecycleDirectoryAsync(string rootDirectory, string caseId, CancellationToken cancellationToken)
    {
        EnsureCanonicalLifecycleId(caseId);
        return DeleteOwnedDirectoryAsync(Path.Combine(RunDirectory, rootDirectory, caseId), cancellationToken);
    }

    private async Task DeleteOwnedDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        _layout.EnsureOwnedRunDirectory(RunDirectory);
        _layout.EnsureSafePath(directory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CleanupTimeout);
        await _cleaner.DeleteAsync(directory, timeout.Token);
    }

    private void ValidateOwnedRunDirectory()
    {
        try
        {
            _layout.EnsureOwnedRunDirectory(RunDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PrivateRunCleanupException(PrivateRunCleanupStage.RunValidation);
        }
    }
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

    internal string ResolveWebOwnedPath(string runDirectory, string relativeRoot)
    {
        EnsureOwnedRunDirectory(runDirectory);
        var childDirectory = NormalizeOwnedRoot(relativeRoot) switch
        {
            "web-source" => "web-source",
            "web-runtime" => "web-runtime",
            _ => throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage)
        };
        return ResolveOwnedPath(runDirectory, childDirectory);
    }

    internal string ResolveCacheOwnedPath(string relativeRoot)
    {
        var childDirectory = NormalizeOwnedRoot(relativeRoot) switch
        {
            ".e2e-private/npm-cache" => "npm-cache",
            ".e2e-private/browsers" => "browsers",
            _ => throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage)
        };
        return ResolveOwnedPath(PrivateRoot, childDirectory);
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

    private string ResolveOwnedPath(string ownerRoot, string childDirectory)
    {
        var resolvedPath = Path.GetFullPath(Path.Combine(ownerRoot, childDirectory));
        if (!IsStrictDescendant(ownerRoot, resolvedPath))
        {
            throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage);
        }

        EnsureSafePath(ownerRoot);
        EnsureSafePath(resolvedPath);
        return resolvedPath;
    }

    private static string NormalizeOwnedRoot(string relativeRoot)
    {
        if (string.IsNullOrWhiteSpace(relativeRoot))
        {
            throw new InvalidOperationException(PrivateRunDirectoryLease.PathValidationMessage);
        }

        return relativeRoot.Replace('\\', '/');
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
    private static readonly TimeSpan SharingViolationRetryDelay = TimeSpan.FromMilliseconds(100);
    private const int SharingViolationHResult = unchecked((int)0x80070020);
    private readonly IRunDirectoryFileSystem _fileSystem;

    internal FileSystemRunDirectoryCleaner(IRunDirectoryFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem ?? new RunDirectoryFileSystem();
    }

    public Task DeleteAsync(string runDirectory, CancellationToken cancellationToken) =>
        DeleteDirectoryAsync(runDirectory, cancellationToken);

    private async Task DeleteDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_fileSystem.DirectoryExists(directory))
        {
            return;
        }

        string[] entries;
        try
        {
            entries = _fileSystem.GetEntries(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PrivateRunCleanupException(PrivateRunCleanupStage.Enumeration);
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try
            {
                attributes = _fileSystem.GetAttributes(entry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new PrivateRunCleanupException(PrivateRunCleanupStage.Attributes);
            }
            if ((attributes & FileAttributes.Directory) != 0 &&
                (attributes & FileAttributes.ReparsePoint) == 0)
            {
                await DeleteDirectoryAsync(entry, cancellationToken);
            }
            else if ((attributes & FileAttributes.Directory) != 0)
            {
                await DeleteAsync(entry, isDirectory: true, PrivateRunCleanupStage.EntryDelete, cancellationToken);
            }
            else
            {
                await DeleteAsync(entry, isDirectory: false, PrivateRunCleanupStage.EntryDelete, cancellationToken);
            }
            await Task.Yield();
        }

        cancellationToken.ThrowIfCancellationRequested();
        await DeleteAsync(directory, isDirectory: true, PrivateRunCleanupStage.ParentDelete, cancellationToken);
    }

    private async Task DeleteAsync(
        string path,
        bool isDirectory,
        PrivateRunCleanupStage stage,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (isDirectory)
                {
                    _fileSystem.DeleteDirectory(path);
                }
                else
                {
                    _fileSystem.DeleteFile(path);
                }

                return;
            }
            catch (IOException exception) when (stage == PrivateRunCleanupStage.EntryDelete &&
                                               exception.HResult == SharingViolationHResult)
            {
                try
                {
                    await Task.Delay(SharingViolationRetryDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw new PrivateRunCleanupException(PrivateRunCleanupStage.EntryDelete);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new PrivateRunCleanupException(stage);
            }
        }
    }
}




internal interface IRunDirectoryFileSystem
{
    bool DirectoryExists(string path);
    string[] GetEntries(string path);
    FileAttributes GetAttributes(string path);
    void DeleteDirectory(string path);
    void DeleteFile(string path);
}

internal sealed class RunDirectoryFileSystem : IRunDirectoryFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string[] GetEntries(string path) => Directory.GetFileSystemEntries(path);
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public void DeleteDirectory(string path) => Directory.Delete(path);
    public void DeleteFile(string path) => File.Delete(path);
}
