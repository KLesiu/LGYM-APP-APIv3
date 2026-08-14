using Microsoft.Playwright;

namespace LgymApi.E2ETests.Browser;

internal static class BrowserScenarioLifecycleAdapter
{
    internal static Task<BrowserScenarioLease> CreateAsync(
        BrowserRunLease browserRun,
        int actionTimeoutMilliseconds,
        CancellationToken cancellationToken = default) =>
        BrowserScenarioLease.CreateForLifecycleAsync(browserRun, actionTimeoutMilliseconds, cancellationToken);
}

internal sealed class BrowserScenarioLease : IAsyncDisposable
{
    internal const string BaseUrl = "http://localhost:8083";
    internal const string Locale = "en-US";
    internal const string SetupMessage = "E2E browser scenario setup failed.";
    internal const string CleanupMessage = "E2E browser scenario cleanup failed.";

    private readonly IPage _page;
    private readonly IBrowserContext _context;
    private readonly TimeSpan _cleanupTimeout;
    private readonly object _cleanupLock = new();
    private Task? _cleanup;

    private BrowserScenarioLease(IPage page, IBrowserContext context, int actionTimeoutMilliseconds)
    {
        _page = page;
        _context = context;
        _cleanupTimeout = TimeSpan.FromMilliseconds(actionTimeoutMilliseconds);
    }

    internal IPage Page => _page;

    internal IBrowserContext Context => _context;

    internal static async Task<BrowserScenarioLease> CreateAsync(
        BrowserRunLease browserRun,
        int actionTimeoutMilliseconds,
        CancellationToken cancellationToken = default) =>
        await CreateAsync(browserRun, actionTimeoutMilliseconds, retainPartialCleanup: false, cancellationToken);

    internal static async Task<BrowserScenarioLease> CreateForLifecycleAsync(
        BrowserRunLease browserRun,
        int actionTimeoutMilliseconds,
        CancellationToken cancellationToken = default) =>
        await CreateAsync(browserRun, actionTimeoutMilliseconds, retainPartialCleanup: true, cancellationToken);

    private static async Task<BrowserScenarioLease> CreateAsync(
        BrowserRunLease browserRun,
        int actionTimeoutMilliseconds,
        bool retainPartialCleanup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(browserRun);
        if (actionTimeoutMilliseconds is < 100 or > 120_000)
        {
            throw new InvalidOperationException(BrowserRunLease.ConfigurationMessage);
        }

        IBrowserContext? context = null;
        try
        {
            context = await browserRun.NewContextAsync(new BrowserNewContextOptions
            {
                BaseURL = BaseUrl,
                Locale = Locale
            }, cancellationToken);
            context.SetDefaultTimeout(actionTimeoutMilliseconds);
            var pageCreation = context.NewPageAsync();
            IPage page;
            try
            {
                page = await pageCreation.WaitAsync(TimeSpan.FromMilliseconds(actionTimeoutMilliseconds), cancellationToken);
            }
            catch (TimeoutException)
            {
                var partialClose = await ClosePartialContextAsync(context, actionTimeoutMilliseconds, retainPartialCleanup);
                throw new BrowserRetainedOperationException(
                    SetupMessage,
                    Task.WhenAll(ObserveLatePageAsync(pageCreation, actionTimeoutMilliseconds), partialClose));
            }

            return new BrowserScenarioLease(page, context, actionTimeoutMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is PlaywrightException or BrowserRetainedOperationException)
        {
            Task partialClose = Task.CompletedTask;
            if (context is not null)
            {
                partialClose = await ClosePartialContextAsync(context, actionTimeoutMilliseconds, retainPartialCleanup);
            }

            if (exception is BrowserRetainedOperationException retained)
            {
                throw new BrowserRetainedOperationException(SetupMessage, Task.WhenAll(retained.TerminalObservation, partialClose));
            }

            throw new InvalidOperationException(SetupMessage);
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

    public override string ToString() => "<browser-scenario-lease>";

    private static async Task<Task> ClosePartialContextAsync(
        IBrowserContext context,
        int timeoutMilliseconds,
        bool retainToTerminal)
    {
        Task rawClose;
        try
        {
            rawClose = context.CloseAsync();
        }
        catch (Exception)
        {
            return Task.CompletedTask;
        }

        try
        {
            await rawClose.WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
            return Task.CompletedTask;
        }
        catch (TimeoutException)
        {
            return ObserveLatePartialCloseAsync(rawClose);
        }
        catch (PlaywrightException)
        {
            return Task.CompletedTask;
        }
    }

    private static async Task ObserveLatePageAsync(Task<IPage> pageCreation, int timeoutMilliseconds)
    {
        try
        {
            var page = await pageCreation;
            await page.CloseAsync().WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        }
        catch (Exception)
        {
        }
    }

    private static async Task ObserveLatePartialCloseAsync(Task rawClose)
    {
        try
        {
            await rawClose;
        }
        catch (Exception)
        {
        }
    }

    private async Task CleanupAsync()
    {
        var cleanupFailed = false;
        try
        {
            await _page.CloseAsync().WaitAsync(_cleanupTimeout);
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            cleanupFailed = true;
        }

        try
        {
            await _context.CloseAsync().WaitAsync(_cleanupTimeout);
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            cleanupFailed = true;
        }

        if (cleanupFailed)
        {
            throw new InvalidOperationException(CleanupMessage);
        }
    }
}
