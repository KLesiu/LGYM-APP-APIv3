namespace LgymApi.E2ETests.Harness;

internal sealed record ExternalApiHostCleanupResult(
    ExternalApiHostCleanupReceipt Receipt,
    bool HangfireServerStartObserved);

internal static class ExternalApiHostCleanup
{
    internal const string ProcessCategory = "api-process";
    private const string RuntimeCategory = "runtime-configuration";
    private const string DatabaseCategory = "postgresql";

    internal static async Task<ExternalApiHostCleanupResult> DisposeAsync(
        IExternalApiProcess? process,
        IAsyncDisposable? runtime,
        IAsyncDisposable database)
    {
        var categories = new List<string>(3);
        var failureCount = 0;
        failureCount += await DisposeResourceAsync(process, ProcessCategory, categories);
        var processExit = await ObserveProcessExitAsync(process);
        failureCount += processExit.FailureCount;
        failureCount += await DisposeResourceAsync(runtime, RuntimeCategory, categories);
        failureCount += await DisposeResourceAsync(database, DatabaseCategory, categories);
        return new ExternalApiHostCleanupResult(
            new ExternalApiHostCleanupReceipt(categories, failureCount),
            processExit.HangfireServerStartObserved);
    }

    internal static ExternalApiHostCleanupException Merge(
        ExternalApiHostCleanupException first,
        ExternalApiHostCleanupReceipt second) =>
        new(new ExternalApiHostCleanupReceipt(
            first.Receipt.AttemptedCategories.Concat(second.AttemptedCategories).ToArray(),
            first.Receipt.FailureCount + second.FailureCount));

    private static async Task<int> DisposeResourceAsync(
        IAsyncDisposable? resource,
        string category,
        ICollection<string> categories)
    {
        if (resource is null)
        {
            return 0;
        }

        categories.Add(category);
        try
        {
            await resource.DisposeAsync();
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private static async Task<(int FailureCount, bool HangfireServerStartObserved)> ObserveProcessExitAsync(
        IExternalApiProcess? process)
    {
        if (process is null)
        {
            return (0, false);
        }

        try
        {
            var exit = await process.Exit.WaitAsync(process.ExitObservationTimeout);
            return (0, exit.HangfireServerStartObserved);
        }
        catch (Exception)
        {
            return (1, false);
        }
    }
}
