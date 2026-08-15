using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed class PinnedWebSourceStager
{
    internal const string SourceValidationMessage = ExternalGitWorktreeInspector.SourceValidationMessage;
    internal const string CleanupMessage = "The pinned web source staging artifacts could not be cleaned safely.";
    private readonly IExternalGitCommandRunner _git;
    private readonly ExternalGitWorktreeInspector _inspector;
    private readonly GitTreeManifestReader _manifestReader;
    private readonly IPinnedWebSourceStagingArtifactCleaner _artifactCleaner;

    internal PinnedWebSourceStager(string gitExecutable) :
        this(new ExternalGitCommandRunner(gitExecutable))
    {
    }

    internal PinnedWebSourceStager(
        IExternalGitCommandRunner git,
        IPinnedWebSourceStagingArtifactCleaner? artifactCleaner = null)
    {
        _git = git;
        _inspector = new ExternalGitWorktreeInspector(_git);
        _manifestReader = new GitTreeManifestReader(_git);
        _artifactCleaner = artifactCleaner ?? new StagingArtifactCleaner();
    }

    internal Task<PinnedWebSourceStage> StageAsync(
        E2EOptions options,
        PrivateRunDirectoryLease runLease,
        CancellationToken cancellationToken = default) =>
        StageAsync(
            new PinnedWebSourceRequest(
                options.WebSource.SourcePath ?? string.Empty,
                options.WebSource.CommitSha,
                runLease,
                TimeSpan.FromSeconds(options.Timeouts.WebStartupSeconds),
                TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds)),
            cancellationToken);

    internal async Task<PinnedWebSourceStage> StageAsync(
        PinnedWebSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        var timeouts = new ExternalGitCommandTimeouts(request.ExecutionTimeout, request.ShutdownTimeout);
        var destinationPath = request.RunLease.ResolveWebOwnedPath("web-source");
        var archivePath = Path.Combine(request.RunLease.RunDirectory, "web-source.tar");
        ExternalGitWorktree? worktree = null;
        try
        {
            worktree = await _inspector.InspectAsync(
                request.SourcePath,
                request.CommitSha,
                timeouts,
                cancellationToken);
            var manifest = await _manifestReader.ReadAsync(worktree, timeouts, cancellationToken);
            await CreateArchiveAsync(worktree, archivePath, timeouts, cancellationToken);
            await PinnedWebSourceArchiveExtractor.ExtractAsync(
                archivePath,
                destinationPath,
                manifest,
                worktree.ObjectFormat,
                cancellationToken);
            File.Delete(archivePath);
            await _inspector.EnsureUnchangedAsync(worktree, timeouts, cancellationToken);
            return new PinnedWebSourceStage(
                destinationPath,
                new PinnedWebSourceReceipt(
                    worktree.PinnedCommitSha,
                    SourceStatePreserved: true,
                    manifest.Entries.Count,
                    manifest.Sha256,
                    ManifestMatched: true,
                    TemporaryArchiveRemoved: true));
        }
        catch (Exception stagingFailure)
        {
            var artifactCleanupFailed = false;
            Exception? stateValidationFailure = null;
            try
            {
                _artifactCleaner.DeleteIfPresent(archivePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                artifactCleanupFailed = true;
            }

            try
            {
                _artifactCleaner.DeleteIfPresent(destinationPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                artifactCleanupFailed = true;
            }

            try
            {
                if (worktree is not null)
                {
                    using var stateValidation = new CancellationTokenSource(request.ShutdownTimeout);
                    await _inspector.EnsureUnchangedAsync(worktree, timeouts, stateValidation.Token);
                }
            }
            catch (Exception exception)
            {
                stateValidationFailure = exception;
            }

            if (request.DisposeRunLeaseOnFailure)
            {
                try
                {
                    await request.RunLease.DisposeAsync();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    artifactCleanupFailed = true;
                }
            }

            if (artifactCleanupFailed)
            {
                throw new InvalidOperationException(CleanupMessage);
            }

            if (stateValidationFailure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(stateValidationFailure).Throw();
            }

            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(stagingFailure).Throw();
            throw;
        }
    }

    private async Task CreateArchiveAsync(
        ExternalGitWorktree worktree,
        string archivePath,
        ExternalGitCommandTimeouts timeouts,
        CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(
            worktree.SourcePath,
            ["archive", "--format=tar", $"--output={archivePath}", worktree.PinnedCommitSha],
            ExternalGitCommandRunner.DiscardAsync,
            timeouts,
            cancellationToken);
        if (result.ExitCode != 0 || !File.Exists(archivePath))
        {
            throw new InvalidOperationException(SourceValidationMessage);
        }
    }

    private sealed class StagingArtifactCleaner : IPinnedWebSourceStagingArtifactCleaner
    {
        public void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                DeleteDirectory(path);
            }
        }

        private static void DeleteDirectory(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(directory);
                return;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    DeleteDirectory(entry);
                }
                else
                {
                    File.SetAttributes(entry, FileAttributes.Normal);
                    File.Delete(entry);
                }
            }

            Directory.Delete(directory);
        }
    }
}
