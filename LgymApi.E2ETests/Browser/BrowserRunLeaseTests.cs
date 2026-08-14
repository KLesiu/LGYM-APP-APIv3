using LgymApi.E2ETests.Harness;
using LgymApi.E2ETests.Lifecycle;
using Microsoft.Playwright;

namespace LgymApi.E2ETests.Browser;

[TestFixture]
[Category("Task6Browser")]
[Category("WebHarness")]
public sealed class BrowserRunLeaseTests
{
    [Test]
    public async Task Create_uses_private_browser_root_and_headless_bounded_Chromium_then_restores_environment()
    {
        var original = Environment.GetEnvironmentVariable(BrowserRunLease.BrowsersPathVariable);
        var repositoryRoot = RepositoryRoot.Find();
        await using var paths = CreatePaths(repositoryRoot);
        var factory = new RecordingBrowserRuntimeFactory();

        try
        {
            Environment.SetEnvironmentVariable(BrowserRunLease.BrowsersPathVariable, "task-6-original");
            await using var lease = await BrowserRunLease.CreateAsync(
                new BrowserRunRequest(paths, 15_000),
                factory);

            var expectedBrowserRoot = Path.GetFullPath(Path.Combine(repositoryRoot, ".e2e-private", "browsers"));
            Assert.Multiple(() =>
            {
                Assert.That(factory.EnvironmentAtCreate, Is.EqualTo(expectedBrowserRoot));
                Assert.That(factory.Runtime.EnvironmentAtLaunch, Is.EqualTo(expectedBrowserRoot));
                Assert.That(factory.Runtime.Options!.Headless, Is.True);
                Assert.That(factory.Runtime.Options.Timeout, Is.EqualTo(15_000));
                Assert.That(Environment.GetEnvironmentVariable(BrowserRunLease.BrowsersPathVariable), Is.EqualTo("task-6-original"));
                Assert.That(lease.ToString(), Is.EqualTo("<browser-run-lease>"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(BrowserRunLease.BrowsersPathVariable, original);
        }
    }

    [TestCase(99)]
    [TestCase(120_001)]
    public async Task Create_rejects_invalid_browser_bounds_before_creating_Playwright(int timeoutMilliseconds)
    {
        await using var paths = CreatePaths(RepositoryRoot.Find());
        var factory = new RecordingBrowserRuntimeFactory();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await BrowserRunLease.CreateAsync(new BrowserRunRequest(paths, timeoutMilliseconds), factory));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(BrowserRunLease.ConfigurationMessage));
            Assert.That(factory.CreateCount, Is.Zero);
        });
    }

    [Test]
    public async Task Missing_Chromium_returns_sanitized_prerequisite_failure_and_restores_environment()
    {
        var original = Environment.GetEnvironmentVariable(BrowserRunLease.BrowsersPathVariable);
        await using var paths = CreatePaths(RepositoryRoot.Find());
        var factory = new RecordingBrowserRuntimeFactory
        {
            LaunchException = new PlaywrightException("browser executable missing at private absolute path")
        };

        try
        {
            Environment.SetEnvironmentVariable(BrowserRunLease.BrowsersPathVariable, null);
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await BrowserRunLease.CreateAsync(new BrowserRunRequest(paths, 15_000), factory));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(BrowserRunLease.PrerequisiteMessage));
                Assert.That(exception.ToString(), Does.Not.Contain("private absolute path"));
                Assert.That(factory.Runtime.Events, Is.EqualTo(new[] { "playwright-dispose" }));
                Assert.That(Environment.GetEnvironmentVariable(BrowserRunLease.BrowsersPathVariable), Is.Null);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(BrowserRunLease.BrowsersPathVariable, original);
        }
    }

    [Test]
    public async Task Dispose_closes_browser_before_Playwright_and_is_idempotent()
    {
        await using var paths = CreatePaths(RepositoryRoot.Find());
        var factory = new RecordingBrowserRuntimeFactory();
        var lease = await BrowserRunLease.CreateAsync(new BrowserRunRequest(paths, 500), factory);

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.That(factory.Runtime.Events, Is.EqualTo(new[] { "browser-close", "playwright-dispose" }));
    }

    [Test]
    public async Task Concurrent_disposal_shares_one_reverse_ordered_cleanup()
    {
        await using var paths = CreatePaths(RepositoryRoot.Find());
        var factory = new RecordingBrowserRuntimeFactory();
        var lease = await BrowserRunLease.CreateAsync(new BrowserRunRequest(paths, 500), factory);

        await Task.WhenAll(lease.DisposeAsync().AsTask(), lease.DisposeAsync().AsTask());

        Assert.That(factory.Runtime.Events, Is.EqualTo(new[] { "browser-close", "playwright-dispose" }));
    }

    [Test]
    public async Task Lifecycle_browser_runs_use_distinct_canonical_runtime_children_while_binaries_stay_run_scoped()
    {
        await using var run = LifecycleRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(),
            ".e2e-private/runs",
            TimeSpan.FromSeconds(2)));
        var firstScenario = run.CreateScenario("browser-runtime-first");
        var secondScenario = run.CreateScenario("browser-runtime-second");
        var firstRuntime = firstScenario.CreateBrowserRuntimeComponent();
        var secondRuntime = secondScenario.CreateBrowserRuntimeComponent();
        var firstFactory = new RecordingBrowserRuntimeFactory();
        var secondFactory = new RecordingBrowserRuntimeFactory();

        await using var first = await BrowserRunLease.CreateAsync(new BrowserRunRequest(run.RunLease, 500)
        {
            RuntimeDirectory = firstRuntime
        }, firstFactory);
        await using var second = await BrowserRunLease.CreateAsync(new BrowserRunRequest(run.RunLease, 500)
        {
            RuntimeDirectory = secondRuntime
        }, secondFactory);

        Assert.Multiple(() =>
        {
            Assert.That(RuntimeDirectoriesAreOwnedBy(firstFactory.Runtime.Options!, firstRuntime), Is.True);
            Assert.That(RuntimeDirectoriesAreOwnedBy(secondFactory.Runtime.Options!, secondRuntime), Is.True);
            Assert.That(RuntimeDirectoriesDiffer(firstFactory.Runtime.Options!, secondFactory.Runtime.Options!), Is.True);
            Assert.That(firstFactory.EnvironmentAtCreate, Is.EqualTo(secondFactory.EnvironmentAtCreate));
        });
    }

    private static PrivateRunDirectoryLease CreatePaths(string repositoryRoot) =>
        PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            repositoryRoot,
            ".e2e-private/runs",
            TimeSpan.FromSeconds(2)));

    private static bool RuntimeDirectoriesAreOwnedBy(
        BrowserTypeLaunchOptions options,
        LifecycleComponentDirectoryLease runtime) =>
        new[] { options.ArtifactsDir, options.DownloadsPath, options.TracesDir }
            .All(path => path is not null &&
                         PrivateRunDirectoryLayout.IsDescendantOrSame(runtime.ComponentDirectory, path));

    private static bool RuntimeDirectoriesDiffer(
        BrowserTypeLaunchOptions first,
        BrowserTypeLaunchOptions second) =>
        !string.Equals(first.ArtifactsDir, second.ArtifactsDir, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(first.DownloadsPath, second.DownloadsPath, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(first.TracesDir, second.TracesDir, StringComparison.OrdinalIgnoreCase);
}

internal sealed class RecordingBrowserRuntimeFactory : IBrowserRuntimeFactory
{
    internal int CreateCount { get; private set; }
    internal string? EnvironmentAtCreate { get; private set; }
    internal Exception? LaunchException { get; init; }
    internal RecordingBrowserRuntime Runtime { get; } = new();

    public Task<IBrowserRuntime> CreateAsync()
    {
        CreateCount++;
        EnvironmentAtCreate = Environment.GetEnvironmentVariable(BrowserRunLease.BrowsersPathVariable);
        Runtime.LaunchException = LaunchException;
        return Task.FromResult<IBrowserRuntime>(Runtime);
    }
}

internal sealed class RecordingBrowserRuntime : IBrowserRuntime
{
    internal List<string> Events { get; } = [];
    internal string? EnvironmentAtLaunch { get; private set; }
    internal BrowserTypeLaunchOptions? Options { get; private set; }
    internal Exception? LaunchException { get; set; }

    public Task<IBrowserHandle> LaunchChromiumAsync(BrowserTypeLaunchOptions options)
    {
        EnvironmentAtLaunch = Environment.GetEnvironmentVariable(BrowserRunLease.BrowsersPathVariable);
        Options = options;
        if (LaunchException is not null)
        {
            throw LaunchException;
        }

        return Task.FromResult<IBrowserHandle>(new RecordingBrowserHandle(Events));
    }

    public void Dispose() => Events.Add("playwright-dispose");
}

internal sealed class RecordingBrowserHandle(List<string> events) : IBrowserHandle
{
    public Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions options) =>
        throw new NotSupportedException();

    public Task CloseAsync()
    {
        events.Add("browser-close");
        return Task.CompletedTask;
    }
}
