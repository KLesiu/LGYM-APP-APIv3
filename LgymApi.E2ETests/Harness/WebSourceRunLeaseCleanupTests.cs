namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class WebSourceRunLeaseCleanupTests
{
    [Test]
    public async Task WebSourceRun_caller_cancellation_cleans_private_source_cache_and_run_within_session_bound()
    {
        // Given
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var runner = new Task3NodeNpmCommandRunner { WaitForNpmCancellation = true };
        await using var lease = await CreateLeaseAsync(fixture, runner);
        using var cancellation = new CancellationTokenSource();

        // When
        var installation = lease.EnsureInstalledAsync(cancellation.Token);
        await runner.NpmStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();
        var exception = Assert.ThrowsAsync<TaskCanceledException>(async () => await installation);

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
            Assert.That(Directory.Exists(lease.NpmCacheDirectory), Is.False);
        });
    }

    [Test]
    public async Task WebSourceRun_npm_failure_is_sanitized_and_cleans_owned_artifacts()
    {
        // Given
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        const string secretCanary = "task3-split-secret-canary";
        var runner = new Task3NodeNpmCommandRunner
        {
            NpmExitCode = 1,
            NpmOutput = "environment-key=TASK3_PARENT_CANARY <redacted>"
        };
        var lease = await CreateLeaseAsync(fixture, runner, [secretCanary]);

        try
        {
            // When
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await lease.EnsureInstalledAsync());

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(WebSourceRunLease.InstallationMessage));
                Assert.That(exception.ToString(), Does.Not.Contain(secretCanary));
                Assert.That(runner.Requests.Single(request => request.Arguments.Contains("ci")).SecretCanaries, Does.Contain(secretCanary));
                Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
                Assert.That(Directory.Exists(lease.NpmCacheDirectory), Is.False);
            });
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [Test]
    public async Task WebSourceRun_cleanup_failure_is_bounded_sanitized_and_still_removes_the_owned_run()
    {
        // Given
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var cacheCleaner = new Task3FailingCacheCleaner();
        var lease = await WebSourceRunLease.CreateAsync(
            fixture.CreateRequest(),
            new WebSourceRunDependencies
            {
                Stager = new Task3WebSourceStager(),
                ToolResolver = fixture.CreateToolResolver(),
                CommandRunner = new Task3NodeNpmCommandRunner(),
                CacheCleaner = cacheCleaner
            });

        // When
        var exception = Assert.ThrowsAsync<WebSourceRunCleanupException>(async () => await lease.DisposeAsync());

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(WebSourceRunLease.CleanupMessage));
            Assert.That(exception.Stage, Is.EqualTo(PrivateRunCleanupStage.CacheDelete));
            Assert.That(cacheCleaner.ObservedTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(lease.CleanupStage, Is.EqualTo(PrivateRunCleanupStage.CacheDelete));
            Assert.That(Directory.Exists(lease.RunDirectory), Is.False);
        });
    }

    [TestCase(nameof(PrivateRunCleanupStage.Enumeration))]
    [TestCase(nameof(PrivateRunCleanupStage.Attributes))]
    [TestCase(nameof(PrivateRunCleanupStage.EntryDelete))]
    [TestCase(nameof(PrivateRunCleanupStage.ParentDelete))]
    public async Task WebSourceRun_cleanup_propagates_the_sanitized_run_stage(string stageName)
    {
        var stage = Enum.Parse<PrivateRunCleanupStage>(stageName);
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var lease = await WebSourceRunLease.CreateAsync(
            fixture.CreateRequest(),
            new WebSourceRunDependencies
            {
                Stager = new Task3WebSourceStager(),
                ToolResolver = fixture.CreateToolResolver(),
                CommandRunner = new Task3NodeNpmCommandRunner(),
                RunDirectoryCleaner = new FileSystemRunDirectoryCleaner(new FailingRunDirectoryFileSystem(stage))
            });

        var exception = Assert.ThrowsAsync<WebSourceRunCleanupException>(async () => await lease.DisposeAsync());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(WebSourceRunLease.CleanupMessage));
            Assert.That(exception.Stage, Is.EqualTo(stage));
            Assert.That(lease.CleanupStage, Is.EqualTo(stage));
        });
    }

    private static Task<WebSourceRunLease> CreateLeaseAsync(
        Task3WebSourceRunFixture fixture,
        Task3NodeNpmCommandRunner runner,
        IReadOnlyList<string>? secretCanaries = null) =>
        WebSourceRunLease.CreateAsync(
            fixture.CreateRequest(secretCanaries),
            new WebSourceRunDependencies
            {
                Stager = new Task3WebSourceStager(),
                ToolResolver = fixture.CreateToolResolver(),
                CommandRunner = runner
            });
}
