namespace LgymApi.E2ETests.Harness;

internal sealed record ExternalApiHostCleanupResult(
    ExternalApiHostCleanupReceipt Receipt,
    bool HangfireServerStartObserved);

internal static class ExternalApiHostCleanup
{
    internal const string ProcessCategory = "api-process";
    internal const string RuntimeCategory = "runtime-configuration";
    internal const string DatabaseCategory = "postgresql";

    internal static async Task<ExternalApiHostCleanupResult> DisposeAsync(
        IExternalApiProcess? process,
        IApiHostRuntimeLease? runtime,
        IApiHostDatabaseLease? database)
    {
        var categories = new List<string>(3);
        var failureCount = 0;
        failureCount += await DisposeResourceAsync(process, ProcessCategory, categories);
        var processExit = await ObserveProcessExitAsync(process);
        failureCount += processExit.FailureCount;
        failureCount += await DisposeResourceAsync(runtime, RuntimeCategory, categories);
        var databaseDisposed = await DisposeDatabaseAsync(database, categories);
        if (!databaseDisposed)
        {
            failureCount++;
        }

        var databaseAbsent = await ConfirmDatabaseAbsentAsync(database, databaseDisposed);
        if (!databaseAbsent)
        {
            failureCount++;
        }

        var processTreeAbsent = process is null || process.ProcessTreeAbsent;
        var runtimeDirectoryAbsent = runtime is null || runtime.RuntimeDirectoryAbsent;
        if (!processTreeAbsent)
        {
            failureCount++;
        }

        if (!runtimeDirectoryAbsent)
        {
            failureCount++;
        }

        return new ExternalApiHostCleanupResult(
            new ExternalApiHostCleanupReceipt(
                processTreeAbsent,
                runtimeDirectoryAbsent,
                databaseAbsent,
                categories,
                failureCount),
            processExit.HangfireServerStartObserved);
    }

    internal static ExternalApiHostCleanupReceipt Merge(
        ExternalApiHostCleanupReceipt first,
        ExternalApiHostCleanupReceipt second)
    {
        var attemptedProcess = second.AttemptedCategories.Contains(ProcessCategory, StringComparer.Ordinal);
        var attemptedRuntime = second.AttemptedCategories.Contains(RuntimeCategory, StringComparer.Ordinal);
        var attemptedDatabase = second.AttemptedCategories.Contains(DatabaseCategory, StringComparer.Ordinal);
        return new ExternalApiHostCleanupReceipt(
            attemptedProcess ? second.ProcessTreeAbsent : first.ProcessTreeAbsent,
            attemptedRuntime ? second.RuntimeDirectoryAbsent : first.RuntimeDirectoryAbsent,
            attemptedDatabase ? second.DatabaseAbsent : first.DatabaseAbsent,
            first.AttemptedCategories.Concat(second.AttemptedCategories).ToArray(),
            first.FailureCount + second.FailureCount);
    }

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

    private static async Task<bool> DisposeDatabaseAsync(
        IApiHostDatabaseLease? database,
        ICollection<string> categories)
    {
        if (database is null)
        {
            return true;
        }

        categories.Add(DatabaseCategory);
        try
        {
            await database.DisposeAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
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

    private static async Task<bool> ConfirmDatabaseAbsentAsync(
        IApiHostDatabaseLease? database,
        bool databaseDisposed)
    {
        if (database is null)
        {
            return true;
        }

        if (database is not IApiHostDatabaseAbsenceObservation observation)
        {
            return databaseDisposed;
        }

        try
        {
            return await observation.ConfirmAbsentAsync();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
