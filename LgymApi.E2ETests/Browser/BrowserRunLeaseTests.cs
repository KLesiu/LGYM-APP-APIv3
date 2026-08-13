using LgymApi.E2ETests.Harness;
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

    private static PrivateRunDirectoryLease CreatePaths(string repositoryRoot) =>
        PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            repositoryRoot,
            ".e2e-private/runs",
            TimeSpan.FromSeconds(2)));
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
