using System.Text.Json;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class PinnedWebSourceStagerTests
{
    [Test]
    public async Task PinnedWebSource_stages_older_commit_from_dirty_worktree_without_changing_source()
    {
        // Given
        await using var fixture = await PinnedWebSourceFixture.CreateAsync();
        await using var lease = fixture.CreateLease();
        var headBefore = await fixture.ReadHeadAsync();
        var statusBefore = await fixture.ReadStatusFingerprintAsync();
        var stager = new PinnedWebSourceStager(fixture.GitExecutable);

        // When
        var stage = await stager.StageAsync(new PinnedWebSourceRequest(
            fixture.SourcePath,
            fixture.PinnedCommit,
            lease,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5)));

        // Then
        var headAfter = await fixture.ReadHeadAsync();
        var statusAfter = await fixture.ReadStatusFingerprintAsync();
        Assert.Multiple(() =>
        {
            Assert.That(headBefore, Is.Not.EqualTo(fixture.PinnedCommit));
            Assert.That(headAfter, Is.EqualTo(headBefore));
            Assert.That(statusAfter, Is.EqualTo(statusBefore));
            Assert.That(File.ReadAllText(Path.Combine(stage.SourceDirectory, "app.txt")), Is.EqualTo("pinned-content\n"));
            Assert.That(File.ReadAllText(Path.Combine(stage.SourceDirectory, "nested", "value.txt")), Is.EqualTo("nested-pinned\n"));
            Assert.That(File.Exists(Path.Combine(stage.SourceDirectory, "current-only.txt")), Is.False);
            Assert.That(File.Exists(Path.Combine(stage.SourceDirectory, ".git")), Is.False);
            Assert.That(stage.Receipt.PinnedCommitSha, Is.EqualTo(fixture.PinnedCommit));
            Assert.That(stage.Receipt.SourceStatePreserved, Is.True);
            Assert.That(stage.Receipt.ManifestEntryCount, Is.EqualTo(2));
            Assert.That(stage.Receipt.ManifestMatched, Is.True);
            Assert.That(stage.Receipt.TemporaryArchiveRemoved, Is.True);
            Assert.That(stage.Receipt.ToString(), Is.EqualTo("<pinned-web-source-receipt>"));
            Assert.That(stage.Receipt.ToString(), Does.Not.Contain(fixture.SourcePath));
            Assert.That(stage.Receipt.ToString(), Does.Not.Contain(lease.RunDirectory));
        });

        WriteSanitizedEvidence(stage.Receipt);
        await lease.DisposeAsync();
        Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
    }

    [Test]
    public async Task PinnedWebSource_rejects_missing_pin_with_sanitized_failure_and_no_partial_stage()
    {
        // Given
        await using var fixture = await PinnedWebSourceFixture.CreateAsync();
        await using var lease = fixture.CreateLease();
        var stager = new PinnedWebSourceStager(fixture.GitExecutable);
        const string absentPin = "1111111111111111111111111111111111111111";

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await stager.StageAsync(new PinnedWebSourceRequest(
                fixture.SourcePath,
                absentPin,
                lease,
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(5))));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceStager.SourceValidationMessage));
            Assert.That(exception.Message, Does.Not.Contain(fixture.SourcePath));
            Assert.That(Directory.Exists(lease.ResolveWebOwnedPath("web-source")), Is.False);
            Assert.That(File.Exists(Path.Combine(lease.RunDirectory, "web-source.tar")), Is.False);
        });
    }

    [Test]
    public async Task PinnedWebSource_rejects_nested_checkout_path_and_bare_repository()
    {
        // Given
        await using var fixture = await PinnedWebSourceFixture.CreateAsync();
        await using var nestedLease = fixture.CreateLease();
        var barePath = await fixture.CreateBareRepositoryAsync();
        var stager = new PinnedWebSourceStager(fixture.GitExecutable);

        // When
        var nestedException = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await stager.StageAsync(CreateRequest(Path.Combine(fixture.SourcePath, "nested"), fixture, nestedLease)));
        await nestedLease.DisposeAsync();
        await using var bareLease = fixture.CreateLease();
        var bareException = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await stager.StageAsync(CreateRequest(barePath, fixture, bareLease)));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(nestedException!.Message, Is.EqualTo(PinnedWebSourceStager.SourceValidationMessage));
            Assert.That(bareException!.Message, Is.EqualTo(PinnedWebSourceStager.SourceValidationMessage));
            Assert.That(nestedException.Message, Does.Not.Contain(fixture.SourcePath));
            Assert.That(bareException.Message, Does.Not.Contain(barePath));
            Assert.That(Directory.Exists(bareLease.ResolveWebOwnedPath("web-source")), Is.False);
        });
    }

    [Test]
    public async Task PinnedWebSource_rejects_blob_object_as_commit_pin()
    {
        // Given
        await using var fixture = await PinnedWebSourceFixture.CreateAsync();
        await using var lease = fixture.CreateLease();
        var blobId = await fixture.ReadPinnedBlobIdAsync();
        var stager = new PinnedWebSourceStager(fixture.GitExecutable);

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await stager.StageAsync(CreateRequest(fixture.SourcePath, fixture, lease, blobId)));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceStager.SourceValidationMessage));
            Assert.That(Directory.Exists(lease.ResolveWebOwnedPath("web-source")), Is.False);
            Assert.That(File.Exists(Path.Combine(lease.RunDirectory, "web-source.tar")), Is.False);
        });
    }

    [Test]
    public async Task PinnedWebSource_rejects_changed_source_after_archive_and_cleans_partial_stage()
    {
        // Given
        await using var fixture = await PinnedWebSourceFixture.CreateAsync();
        await using var lease = fixture.CreateLease();
        var git = new ExternalGitCommandRunner(fixture.GitExecutable);
        var stager = new PinnedWebSourceStager(new MutatingArchiveGitRunner(
            git,
            Path.Combine(fixture.SourcePath, "post-archive.txt")));

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await stager.StageAsync(CreateRequest(fixture.SourcePath, fixture, lease)));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalGitWorktreeInspector.SourceChangedMessage));
            Assert.That(Directory.Exists(lease.ResolveWebOwnedPath("web-source")), Is.False);
            Assert.That(File.Exists(Path.Combine(lease.RunDirectory, "web-source.tar")), Is.False);
        });
    }

    [Test]
    public async Task PinnedWebSource_repeated_cancellation_cleans_each_owned_run()
    {
        // Given
        await using var fixture = await PinnedWebSourceFixture.CreateAsync();
        var stager = new PinnedWebSourceStager(fixture.GitExecutable);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var lease = fixture.CreateLease();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            // When
            var exception = Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await stager.StageAsync(CreateRequest(fixture.SourcePath, fixture, lease), cancellation.Token));

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
                Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
            });
        }
    }

    [Test]
    public async Task PinnedWebSource_rejects_misleading_archive_success_and_cleans_owned_run()
    {
        // Given
        await using var fixture = await PinnedWebSourceFixture.CreateAsync();
        var lease = fixture.CreateLease();
        var stager = new PinnedWebSourceStager(new MissingArchiveGitRunner(
            new ExternalGitCommandRunner(fixture.GitExecutable)));

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await stager.StageAsync(CreateRequest(fixture.SourcePath, fixture, lease)));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(PinnedWebSourceStager.SourceValidationMessage));
            Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
        });
    }

    [Test]
    public async Task PinnedWebSource_timeout_failure_cleans_owned_run()
    {
        // Given
        await using var fixture = await PinnedWebSourceFixture.CreateAsync();
        var lease = fixture.CreateLease();
        var stager = new PinnedWebSourceStager(new TimeoutGitRunner());

        // When
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await stager.StageAsync(CreateRequest(fixture.SourcePath, fixture, lease)));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalGitCommandRunner.TimeoutMessage));
            Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
        });
    }

    private static PinnedWebSourceRequest CreateRequest(
        string sourcePath,
        PinnedWebSourceFixture fixture,
        PrivateRunDirectoryLease lease,
        string? pin = null) =>
        new(
            sourcePath,
            pin ?? fixture.PinnedCommit,
            lease,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(5));

    private static void WriteSanitizedEvidence(PinnedWebSourceReceipt receipt)
    {
        var evidenceDirectory = Path.Combine(RepositoryRoot.Find(), "LgymApi.E2ETests", "TestResults");
        Directory.CreateDirectory(evidenceDirectory);
        var evidence = new
        {
            receipt.PinnedCommitSha,
            receipt.SourceStatePreserved,
            receipt.ManifestEntryCount,
            receipt.ManifestSha256,
            receipt.ManifestMatched,
            receipt.TemporaryArchiveRemoved,
            rawStatusRetained = false,
            sourcePathRetained = false,
            processIdentityRetained = false
        };
        File.WriteAllText(
            Path.Combine(evidenceDirectory, "task-2-issue-434-pinned-expo-playwright-harness.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class MutatingArchiveGitRunner(IExternalGitCommandRunner inner, string filePath)
        : IExternalGitCommandRunner
    {
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
                await File.AppendAllTextAsync(filePath, "changed-after-archive\n", cancellationToken);
            }

            return result;
        }
    }

    private sealed class MissingArchiveGitRunner(IExternalGitCommandRunner inner) : IExternalGitCommandRunner
    {
        public Task<ExternalGitCommandResult<T>> RunAsync<T>(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            Func<Stream, CancellationToken, Task<T>> readStandardOutput,
            ExternalGitCommandTimeouts timeouts,
            CancellationToken cancellationToken = default) =>
            arguments.Any(argument => argument == "archive")
                ? ReturnSuccessAsync(readStandardOutput, cancellationToken)
                : inner.RunAsync(
                    workingDirectory,
                    arguments,
                    readStandardOutput,
                    timeouts,
                    cancellationToken);

        private static async Task<ExternalGitCommandResult<T>> ReturnSuccessAsync<T>(
            Func<Stream, CancellationToken, Task<T>> readStandardOutput,
            CancellationToken cancellationToken)
        {
            await using var output = new MemoryStream();
            return new ExternalGitCommandResult<T>(0, await readStandardOutput(output, cancellationToken));
        }
    }

    private sealed class TimeoutGitRunner : IExternalGitCommandRunner
    {
        public Task<ExternalGitCommandResult<T>> RunAsync<T>(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            Func<Stream, CancellationToken, Task<T>> readStandardOutput,
            ExternalGitCommandTimeouts timeouts,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ExternalGitCommandResult<T>>(
                new InvalidOperationException(ExternalGitCommandRunner.TimeoutMessage));
    }
}
