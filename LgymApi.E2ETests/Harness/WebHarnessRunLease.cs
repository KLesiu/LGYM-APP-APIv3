namespace LgymApi.E2ETests.Harness;

internal sealed record WebHarnessCleanupFacts(
    bool SourceStateMatched,
    bool ProcessTreeAbsent,
    bool StagedSourceAbsent,
    bool BrowserClosed);

internal interface IWebHarnessCleanupLayer : IAsyncDisposable
{
    string Category { get; }

    WebHarnessCleanupFacts Facts { get; }
}

internal sealed record WebHarnessCleanupReceipt(
    IReadOnlyList<string> AttemptedCategories,
    int FailureCount,
    bool SourceStateMatched,
    bool ProcessTreeAbsent,
    bool StagedSourceAbsent,
    bool BrowserClosed)
{
    public override string ToString() => "<web-harness-cleanup>";
}

internal sealed class WebHarnessCleanupException(WebHarnessCleanupReceipt receipt)
    : InvalidOperationException(WebHarnessRunLease.CleanupFailureMessage)
{
    internal WebHarnessCleanupReceipt Receipt { get; } = receipt;
}

internal sealed class WebHarnessRunLease : IAsyncDisposable
{
    internal const string CleanupFailureMessage = "E2E web harness cleanup failed.";

    private readonly IWebHarnessCleanupLayer _source;
    private readonly IReadOnlyList<IWebHarnessCleanupLayer> _expo;
    private readonly IWebHarnessCleanupLayer? _browser;
    private readonly IReadOnlyList<IWebHarnessCleanupLayer> _scenarios;
    private readonly TimeSpan _shutdownTimeout;
    private readonly object _sync = new();
    private Task? _cleanup;

    private WebHarnessRunLease(
        IWebHarnessCleanupLayer source,
        IReadOnlyList<IWebHarnessCleanupLayer> expo,
        IWebHarnessCleanupLayer? browser,
        IReadOnlyList<IWebHarnessCleanupLayer> scenarios,
        TimeSpan shutdownTimeout)
    {
        _source = source;
        _expo = expo;
        _browser = browser;
        _scenarios = scenarios;
        _shutdownTimeout = shutdownTimeout;
    }

    internal WebHarnessCleanupReceipt CleanupReceipt { get; private set; } =
        new([], 0, false, false, false, false);

    internal static WebHarnessRunLease Create(
        IWebHarnessCleanupLayer source,
        IReadOnlyList<IWebHarnessCleanupLayer> expo,
        IWebHarnessCleanupLayer? browser,
        IReadOnlyList<IWebHarnessCleanupLayer> scenarios,
        TimeSpan shutdownTimeout)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(expo);
        ArgumentNullException.ThrowIfNull(scenarios);
        if (shutdownTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(CleanupFailureMessage);
        }

        return new WebHarnessRunLease(source, expo, browser, scenarios, shutdownTimeout);
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _cleanup ??= CleanupAsync();
            return new ValueTask(_cleanup);
        }
    }

    public override string ToString() => "<web-harness-run-lease>";

    private async Task CleanupAsync()
    {
        var categories = new List<string>();
        var failures = 0;
        var cleanupStarted = DateTime.UtcNow;

        foreach (var scenario in _scenarios.Reverse())
        {
            failures += await DisposeLayerAsync(scenario, categories);
        }

        if (_browser is not null)
        {
            failures += await DisposeLayerAsync(_browser, categories);
        }

        foreach (var expo in _expo.Reverse())
        {
            failures += await DisposeLayerAsync(expo, categories);
        }

        categories.Add(_source.Category);
        var sourceFailure = await DisposeLayerAsync(_source);
        if (sourceFailure != 0)
        {
            var remaining = _shutdownTimeout - (DateTime.UtcNow - cleanupStarted);
            if (remaining > TimeSpan.Zero)
            {
                var retryFailure = await DisposeLayerAsync(_source, remaining);
                sourceFailure = retryFailure == 0 ? 0 : sourceFailure + retryFailure;
            }
        }

        failures += sourceFailure;
        CleanupReceipt = new WebHarnessCleanupReceipt(
            categories,
            failures,
            _source.Facts.SourceStateMatched,
            _expo.All(layer => layer.Facts.ProcessTreeAbsent),
            _source.Facts.StagedSourceAbsent,
            _browser?.Facts.BrowserClosed ?? true);
        if (failures != 0)
        {
            throw new WebHarnessCleanupException(CleanupReceipt);
        }
    }

    private static async Task<int> DisposeLayerAsync(
        IWebHarnessCleanupLayer layer,
        ICollection<string> categories)
    {
        categories.Add(layer.Category);
        return await DisposeLayerAsync(layer);
    }

    private static async Task<int> DisposeLayerAsync(
        IWebHarnessCleanupLayer layer,
        TimeSpan? timeout = null)
    {
        try
        {
            var disposal = layer.DisposeAsync().AsTask();
            if (timeout is not null)
            {
                await disposal.WaitAsync(timeout.Value);
            }
            else
            {
                await disposal;
            }

            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
