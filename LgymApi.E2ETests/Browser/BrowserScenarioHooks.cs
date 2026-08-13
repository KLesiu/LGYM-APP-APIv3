using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Harness;
using Reqnroll;

namespace LgymApi.E2ETests.Browser;

[Binding]
public sealed class BrowserScenarioHooks
{
    internal const int RunBeforeOrder = 100;
    internal const int ScenarioBeforeOrder = 200;
    internal const int ScenarioAfterOrder = 800;
    internal const int RunAfterOrder = 900;

    private BrowserScenarioLease? _scenario;

    [BeforeTestRun(Order = RunBeforeOrder)]
    public static async Task BeforeBrowserRunAsync()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, repositoryRoot);
        var privatePaths = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            repositoryRoot,
            options.Runtime.PrivateRunRoot,
            TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds)));
        try
        {
            var run = new BrowserRunState(
                privatePaths,
                options.Timeouts.BrowserActionMilliseconds);
            BrowserRunStateHolder.Set(run);
            await Task.CompletedTask;
        }
        catch
        {
            await privatePaths.DisposeAsync();
            throw;
        }
    }

    [BeforeScenario("@browser", Order = ScenarioBeforeOrder)]
    public async Task BeforeBrowserScenarioAsync()
    {
        var run = BrowserRunStateHolder.Get();
        var browser = await run.GetBrowserAsync();
        _scenario = await BrowserScenarioLease.CreateAsync(browser, run.ActionTimeoutMilliseconds);
    }

    [AfterScenario("@browser", Order = ScenarioAfterOrder)]
    public async Task AfterBrowserScenarioAsync()
    {
        if (_scenario is null)
        {
            return;
        }

        try
        {
            await _scenario.DisposeAsync();
        }
        finally
        {
            _scenario = null;
        }
    }

    [AfterTestRun(Order = RunAfterOrder)]
    public static async Task AfterBrowserRunAsync()
    {
        var run = BrowserRunStateHolder.Take();
        if (run is not null)
        {
            await run.DisposeAsync();
        }
    }
}

internal sealed class BrowserRunState(
    PrivateRunDirectoryLease privatePaths,
    int actionTimeoutMilliseconds) : IAsyncDisposable
{
    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private BrowserRunLease? _browser;

    internal int ActionTimeoutMilliseconds { get; } = actionTimeoutMilliseconds;

    internal async Task<BrowserRunLease> GetBrowserAsync()
    {
        await _browserLock.WaitAsync();
        try
        {
            _browser ??= await BrowserRunLease.CreateAsync(new BrowserRunRequest(
                privatePaths,
                ActionTimeoutMilliseconds));
            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync();
            }
        }
        finally
        {
            _browserLock.Dispose();
            await privatePaths.DisposeAsync();
        }
    }
}

internal static class BrowserRunStateHolder
{
    private static readonly object Sync = new();
    private static BrowserRunState? _state;

    internal static void Set(BrowserRunState state)
    {
        lock (Sync)
        {
            if (_state is not null)
            {
                throw new InvalidOperationException("E2E browser run state is already initialized.");
            }

            _state = state;
        }
    }

    internal static BrowserRunState Get()
    {
        lock (Sync)
        {
            return _state ?? throw new InvalidOperationException("E2E browser run state is unavailable.");
        }
    }

    internal static BrowserRunState? Take()
    {
        lock (Sync)
        {
            var state = _state;
            _state = null;
            return state;
        }
    }
}
