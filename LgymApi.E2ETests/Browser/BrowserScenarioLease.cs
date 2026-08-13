using Microsoft.Playwright;

namespace LgymApi.E2ETests.Browser;

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
        int actionTimeoutMilliseconds)
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
            });
            context.SetDefaultTimeout(actionTimeoutMilliseconds);
            var page = await context.NewPageAsync();
            return new BrowserScenarioLease(page, context, actionTimeoutMilliseconds);
        }
        catch (PlaywrightException)
        {
            if (context is not null)
            {
                await ClosePartialContextAsync(context, actionTimeoutMilliseconds);
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

    private static async Task ClosePartialContextAsync(IBrowserContext context, int timeoutMilliseconds)
    {
        try
        {
            await context.CloseAsync().WaitAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            return;
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
