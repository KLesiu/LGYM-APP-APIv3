using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

internal sealed partial class OwnedExternalProcessLease
{
    private Task<OwnedExternalProcessCleanupReceipt> EnsureCleanupAsync()
    {
        lock (_cleanupSync)
        {
            return _cleanup ??= CleanupCoreAsync();
        }
    }

    private async Task<OwnedExternalProcessCleanupReceipt> CleanupCoreAsync()
    {
        var cleanupStopwatch = Stopwatch.StartNew();
        using var cleanupCancellation = new CancellationTokenSource(_request.ShutdownTimeout);
        try
        {
            RefreshRetainedIdentities(cleanupCancellation.Token);
        }
        catch (Exception exception) when (IsBoundedInspectionFailure(exception))
        {
            Interlocked.Exchange(ref _inspectionCompleted, 0);
        }

        var identities = CapturedIdentities;
        var exactAbsenceProven = false;
        try
        {
            cleanupCancellation.Token.ThrowIfCancellationRequested();
            var terminationReceipt = await _termination.TerminateAsync(
                _process,
                RootIdentity,
                identities,
                _standardOutput,
                _standardError,
                _standardOutputTask,
                _standardErrorTask,
                _drainCancellation,
                Remaining(cleanupStopwatch));
            exactAbsenceProven = terminationReceipt.Cleanup.AllAbsentOrReused;
        }
        catch (Exception exception) when (IsTerminationFailure(exception))
        {
            exactAbsenceProven = await ProveExactAbsenceWithinBoundAsync(
                identities,
                cleanupCancellation.Token);
        }

        var drainCompleted = await CompleteDrainsWithinBoundAsync(cleanupCancellation.Token);
        var output = SnapshotOutput();
        var receipt = new OwnedExternalProcessCleanupReceipt(
            output.StandardOutput,
            output.StandardError,
            new ProcessCleanupReceipt(identities, exactAbsenceProven),
            drainCompleted,
            Volatile.Read(ref _inspectionCompleted) == 1);
        CleanupReceipt = receipt;
        return receipt;
    }

    private void RefreshRetainedIdentities(CancellationToken cancellationToken)
    {
        var captured = WindowsProcessTree.CaptureFromKnownRoots(
            CapturedIdentities,
            cancellationToken,
            _parentProcessIdReader);
        lock (_identitySync)
        {
            foreach (var identity in captured)
            {
                if (_retainedIdentities.Any(existing =>
                        existing.ProcessId == identity.ProcessId &&
                        existing.StartTimeUtc == identity.StartTimeUtc))
                {
                    continue;
                }

                _retainedIdentities.Add(identity);
            }
        }
    }

    private async Task<bool> CompleteDrainsWithinBoundAsync(CancellationToken cancellationToken)
    {
        if (_drains.IsCompletedSuccessfully)
        {
            return Volatile.Read(ref _drainCompleted) == 1;
        }

        try
        {
            await _drains.WaitAsync(cancellationToken);
            return Volatile.Read(ref _drainCompleted) == 1;
        }
        catch (Exception exception) when (IsDrainFailure(exception))
        {
            Interlocked.Exchange(ref _drainCompleted, 0);
            _drainCancellation.Cancel();
            return false;
        }
    }

    private static async Task<bool> ProveExactAbsenceWithinBoundAsync(
        IReadOnlyList<ProcessIdentity> identities,
        CancellationToken cancellationToken)
    {
        if (WindowsProcessTree.AllAbsentOrReused(identities))
        {
            return true;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            WindowsProcessTree.TerminateKnownIdentities(identities);
            await WindowsProcessTree.WaitUntilAllAbsentOrReusedAsync(identities, cancellationToken);
            return true;
        }
        catch (Exception exception) when (IsBoundedInspectionFailure(exception))
        {
            return WindowsProcessTree.AllAbsentOrReused(identities);
        }
    }

    private TimeSpan Remaining(Stopwatch cleanupStopwatch)
    {
        var remaining = _request.ShutdownTimeout - cleanupStopwatch.Elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromTicks(1);
    }

    private (ExternalProcessOutput StandardOutput, ExternalProcessOutput StandardError) SnapshotOutput() =>
        _drains.IsCompleted
            ? (_standardOutput.Snapshot(), _standardError.Snapshot())
            : (new ExternalProcessOutput(string.Empty, WasTruncated: false),
                new ExternalProcessOutput(string.Empty, WasTruncated: false));

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        _drainCancellation.Dispose();
        _process.Dispose();
    }

    private static bool IsBoundedInspectionFailure(Exception exception) =>
        exception is ProcessTreeInspectionException or OperationCanceledException or InvalidOperationException;

    private static bool IsDrainFailure(Exception exception) =>
        exception is OperationCanceledException or InvalidOperationException or IOException or AggregateException;

    private static bool IsTerminationFailure(Exception exception) =>
        exception is ExternalProcessCleanupException or OperationCanceledException or InvalidOperationException or
        IOException or AggregateException;
}
