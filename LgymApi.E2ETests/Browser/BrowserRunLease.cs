using LgymApi.E2ETests.Harness;
using LgymApi.E2ETests.Lifecycle;
using Microsoft.Playwright;

namespace LgymApi.E2ETests.Browser;

internal sealed record BrowserRunRequest(
    PrivateRunDirectoryLease PrivatePaths,
    int ActionTimeoutMilliseconds)
{
    internal LifecycleComponentDirectoryLease? RuntimeDirectory { get; init; }
}

internal interface IBrowserHandle
{
    Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions options);

    Task CloseAsync();
}

internal interface IBrowserRuntime : IDisposable
{
    Task<IBrowserHandle> LaunchChromiumAsync(BrowserTypeLaunchOptions options);
}

internal interface IBrowserRuntimeFactory
{
    Task<IBrowserRuntime> CreateAsync();
}

internal sealed class BrowserRunLease : IAsyncDisposable
{
    internal const string BrowsersPathVariable = "PLAYWRIGHT_BROWSERS_PATH";
    internal const string ConfigurationMessage = "E2E browser configuration is invalid.";
    internal const string PrerequisiteMessage =
        "E2E Chromium prerequisite is unavailable. Run the repository browser installer after building the E2E project.";
    internal const string CleanupMessage = "E2E browser cleanup failed.";

    private static readonly SemaphoreSlim EnvironmentLock = new(1, 1);
    private readonly IBrowserRuntime _runtime;
    private readonly IBrowserHandle _browser;
    private readonly TimeSpan _cleanupTimeout;
    private readonly object _cleanupLock = new();
    private Task? _cleanup;

    private BrowserRunLease(
        IBrowserRuntime runtime,
        IBrowserHandle browser,
        TimeSpan cleanupTimeout)
    {
        _runtime = runtime;
        _browser = browser;
        _cleanupTimeout = cleanupTimeout;
    }

    internal static async Task<BrowserRunLease> CreateAsync(
        BrowserRunRequest request,
        IBrowserRuntimeFactory? factory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request.PrivatePaths);
        if (request.ActionTimeoutMilliseconds is < 100 or > 120_000)
        {
            throw new InvalidOperationException(ConfigurationMessage);
        }

        var browserRoot = request.PrivatePaths.ResolveCacheOwnedPath(".e2e-private/browsers");
        var timeout = TimeSpan.FromMilliseconds(request.ActionTimeoutMilliseconds);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await EnvironmentLock.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(PrerequisiteMessage);
        }

        var originalBrowserPath = Environment.GetEnvironmentVariable(BrowsersPathVariable);
        var releaseEnvironment = true;
        try
        {
            Environment.SetEnvironmentVariable(BrowsersPathVariable, browserRoot);
            var initialization = InitializeAsync(factory ?? PlaywrightBrowserRuntimeFactory.Instance, request);
            try
            {
                var initialized = await initialization.WaitAsync(deadline.Token);
                return new BrowserRunLease(initialized.Runtime, initialized.Browser, timeout);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                releaseEnvironment = false;
                throw new BrowserRetainedOperationException(
                    PrerequisiteMessage,
                    ObserveLateInitializationAsync(initialization, timeout, originalBrowserPath));
            }
        }
        catch (Exception exception)
        {
            if (exception is BrowserRetainedOperationException)
            {
                throw;
            }

            if (exception is PlaywrightException)
            {
                throw new InvalidOperationException(PrerequisiteMessage);
            }

            throw;
        }
        finally
        {
            if (releaseEnvironment)
            {
                Environment.SetEnvironmentVariable(BrowsersPathVariable, originalBrowserPath);
                EnvironmentLock.Release();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_cleanupLock)
        {
            _cleanup ??= CleanupAsync();
            return new ValueTask(_cleanup);
        }
    }

    public override string ToString() => "<browser-run-lease>";

    internal async Task<IBrowserContext> NewContextAsync(
        BrowserNewContextOptions options,
        CancellationToken cancellationToken = default)
    {
        var creation = _browser.NewContextAsync(options);
        try
        {
            return await creation.WaitAsync(_cleanupTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new BrowserRetainedOperationException(
                BrowserScenarioLease.SetupMessage,
                ObserveLateContextAsync(creation, _cleanupTimeout));
        }
    }

    private static string? CreateRuntimeDirectory(
        LifecycleComponentDirectoryLease? runtimeDirectory,
        string name)
    {
        if (runtimeDirectory is null)
        {
            return null;
        }

        var path = Path.Combine(runtimeDirectory.ComponentDirectory, name);
        runtimeDirectory.EnsureSafeArtifact(path);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<(IBrowserRuntime Runtime, IBrowserHandle Browser)> InitializeAsync(
        IBrowserRuntimeFactory factory,
        BrowserRunRequest request)
    {
        var runtime = await factory.CreateAsync();
        try
        {
            var browser = await runtime.LaunchChromiumAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Timeout = request.ActionTimeoutMilliseconds,
                ArtifactsDir = CreateRuntimeDirectory(request.RuntimeDirectory, "artifacts"),
                DownloadsPath = CreateRuntimeDirectory(request.RuntimeDirectory, "downloads"),
                TracesDir = CreateRuntimeDirectory(request.RuntimeDirectory, "traces")
            });
            return (runtime, browser);
        }
        catch
        {
            DisposeRuntimeAfterFailedInitialization(runtime);
            throw;
        }
    }

    private static async Task ObserveLateInitializationAsync(
        Task<(IBrowserRuntime Runtime, IBrowserHandle Browser)> initialization,
        TimeSpan timeout,
        string? originalBrowserPath)
    {
        try
        {
            var initialized = await initialization;
            try
            {
                await initialized.Browser.CloseAsync().WaitAsync(timeout);
            }
            catch (Exception)
            {
            }

            DisposeRuntimeAfterFailedInitialization(initialized.Runtime);
        }
        catch (Exception)
        {
        }
        finally
        {
            Environment.SetEnvironmentVariable(BrowsersPathVariable, originalBrowserPath);
            EnvironmentLock.Release();
        }
    }

    private static async Task ObserveLateContextAsync(Task<IBrowserContext> creation, TimeSpan timeout)
    {
        try
        {
            var context = await creation;
            await context.CloseAsync().WaitAsync(timeout);
        }
        catch (Exception)
        {
        }
    }

    private static void DisposeRuntimeAfterFailedInitialization(IBrowserRuntime? runtime)
    {
        try
        {
            runtime?.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private async Task CleanupAsync()
    {
        Exception? closeFailure = null;
        try
        {
            await _browser.CloseAsync().WaitAsync(_cleanupTimeout);
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            closeFailure = exception;
        }
        finally
        {
            _runtime.Dispose();
        }

        if (closeFailure is not null)
        {
            throw new InvalidOperationException(CleanupMessage);
        }
    }
}

internal sealed class BrowserRetainedOperationException(string message, Task terminalObservation)
    : InvalidOperationException(message), IRetainedAsyncFailure
{
    public Task TerminalObservation { get; } = terminalObservation;
}

internal sealed class PlaywrightBrowserRuntimeFactory : IBrowserRuntimeFactory
{
    internal static PlaywrightBrowserRuntimeFactory Instance { get; } = new();

    public async Task<IBrowserRuntime> CreateAsync() =>
        new PlaywrightBrowserRuntime(await Playwright.CreateAsync());
}

internal sealed class PlaywrightBrowserRuntime(IPlaywright playwright) : IBrowserRuntime
{
    public async Task<IBrowserHandle> LaunchChromiumAsync(BrowserTypeLaunchOptions options) =>
        new PlaywrightBrowserHandle(await playwright.Chromium.LaunchAsync(options));

    public void Dispose() => playwright.Dispose();
}

internal sealed class PlaywrightBrowserHandle(IBrowser browser) : IBrowserHandle
{
    public Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions options) =>
        browser.NewContextAsync(options);

    public Task CloseAsync() => browser.CloseAsync();
}
