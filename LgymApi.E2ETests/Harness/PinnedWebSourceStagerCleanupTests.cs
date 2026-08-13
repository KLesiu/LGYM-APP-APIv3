namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class PinnedWebSourceStagerCleanupTests
{
    [Test]
    public async Task PinnedWebSource_cleanup_fault_still_checks_source_state_and_disposes_owned_run()
    {
        await using var fixture = await PinnedWebSourceFixture.CreateAsync();
        var runCleaner = new RecordingRunDirectoryCleaner();
        var lease = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            fixture.OwnerRoot,
            ".e2e-private/runs",
            TimeSpan.FromSeconds(5)), runCleaner);
        var git = new PostArchiveMutatingGitRunner(
            new ExternalGitCommandRunner(fixture.GitExecutable),
            Path.Combine(fixture.SourcePath, "post-archive.txt"));
        var artifacts = new FailingFirstStagingArtifactCleaner();
        var stager = new PinnedWebSourceStager(git, artifacts);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await stager.StageAsync(new PinnedWebSourceRequest(
                fixture.SourcePath,
                fixture.PinnedCommit,
                lease,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5))));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceStager.CleanupMessage));
            Assert.That(exception.ToString(), Does.Not.Contain("tar-cleanup-canary"));
            Assert.That(artifacts.Attempts, Is.EqualTo(2));
            Assert.That(git.PostArchiveStatusChecks, Is.EqualTo(2));
            Assert.That(runCleaner.DeleteAttempts, Is.EqualTo(1));
            Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
        });
    }

    private sealed class PostArchiveMutatingGitRunner(IExternalGitCommandRunner inner, string mutationPath)
        : IExternalGitCommandRunner
    {
        private bool _archiveCreated;

        internal int PostArchiveStatusChecks { get; private set; }

        public async Task<ExternalGitCommandResult<T>> RunAsync<T>(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            Func<Stream, CancellationToken, Task<T>> readStandardOutput,
            ExternalGitCommandTimeouts timeouts,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.RunAsync(
                workingDirectory,
                arguments,
                readStandardOutput,
                timeouts,
                cancellationToken);
            if (arguments.Any(argument => argument == "archive"))
            {
                _archiveCreated = true;
                await File.AppendAllTextAsync(mutationPath, "changed-after-archive\n", cancellationToken);
            }
            else if (_archiveCreated && arguments.FirstOrDefault() == "status")
            {
                PostArchiveStatusChecks++;
            }

            return result;
        }
    }

    private sealed class FailingFirstStagingArtifactCleaner : IPinnedWebSourceStagingArtifactCleaner
    {
        internal int Attempts { get; private set; }

        public void DeleteIfPresent(string path)
        {
            Attempts++;
            if (Attempts == 1)
            {
                throw new IOException("tar-cleanup-canary");
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private sealed class RecordingRunDirectoryCleaner : IRunDirectoryCleaner
    {
        internal int DeleteAttempts { get; private set; }

        public Task DeleteAsync(string runDirectory, CancellationToken cancellationToken)
        {
            DeleteAttempts++;
            Directory.Delete(runDirectory, recursive: true);
            return Task.CompletedTask;
        }
    }
}
