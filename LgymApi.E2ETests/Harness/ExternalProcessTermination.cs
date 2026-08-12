using System.ComponentModel;
using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

internal sealed class ExternalProcessTermination
{
    internal async Task<ExternalProcessFailureReceipt> TerminateAsync(
        Process process,
        ProcessIdentity rootIdentity,
        IReadOnlyList<ProcessIdentity> retainedIdentities,
        BoundedSanitizedStreamCapture standardOutput,
        BoundedSanitizedStreamCapture standardError,
        Task standardOutputTask,
        Task standardErrorTask,
        CancellationTokenSource drainCancellation,
        TimeSpan shutdownTimeout)
    {
        using var shutdownCancellation = new CancellationTokenSource(shutdownTimeout);
        var shutdownToken = shutdownCancellation.Token;
        IReadOnlyList<ProcessIdentity> identities = retainedIdentities;
        try
        {
            WindowsProcessTree.TerminateKnownIdentities(identities);

            await Task.WhenAll(
                    WaitForRootExitAsync(process, rootIdentity, shutdownToken),
                    standardOutputTask,
                    standardErrorTask,
                    WindowsProcessTree.WaitUntilAllAbsentOrReusedAsync(identities, shutdownToken))
                .WaitAsync(shutdownToken);
        }
        catch (Exception exception) when (IsCleanupFailure(exception))
        {
            var receipt = await CompleteUnprovenCleanupAsync(
                process,
                rootIdentity,
                standardOutput,
                standardError,
                standardOutputTask,
                standardErrorTask,
                drainCancellation,
                identities,
                shutdownToken);
            throw new ExternalProcessCleanupException(receipt);
        }

        return new ExternalProcessFailureReceipt(
            standardOutput.Snapshot(),
            standardError.Snapshot(),
            new ProcessCleanupReceipt(identities, AllAbsentOrReused: true));
    }

    internal async Task<ExternalProcessFailureReceipt> CompleteUnprovenCleanupAsync(
        Process process,
        ProcessIdentity? rootIdentity,
        BoundedSanitizedStreamCapture standardOutput,
        BoundedSanitizedStreamCapture standardError,
        Task standardOutputTask,
        Task standardErrorTask,
        CancellationTokenSource drainCancellation,
        IReadOnlyList<ProcessIdentity> knownIdentities,
        CancellationToken shutdownToken)
    {
        try
        {
            TryKillRootTree(process);
            WindowsProcessTree.TerminateKnownIdentities(knownIdentities);
            var rootExitTask = rootIdentity is null
                ? process.WaitForExitAsync(shutdownToken)
                : WaitForRootExitAsync(process, rootIdentity, shutdownToken);
            await Task.WhenAll(
                    rootExitTask,
                    standardOutputTask,
                    standardErrorTask,
                    WindowsProcessTree.WaitUntilAllAbsentOrReusedAsync(knownIdentities, shutdownToken))
                .WaitAsync(shutdownToken);
        }
        catch (Exception exception) when (IsCleanupFailure(exception))
        {
            drainCancellation.Cancel();
        }

        return new ExternalProcessFailureReceipt(
            standardOutput.Snapshot(),
            standardError.Snapshot(),
            new ProcessCleanupReceipt(knownIdentities, AllAbsentOrReused: false));
    }

    private static async Task WaitForRootExitAsync(
        Process process,
        ProcessIdentity rootIdentity,
        CancellationToken cancellationToken)
    {
        if (WindowsProcessTree.AllAbsentOrReused([rootIdentity]))
        {
            return;
        }

        await process.WaitForExitAsync(cancellationToken);
    }

    private static void TryKillRootTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static bool IsCleanupFailure(Exception exception) =>
        exception is ProcessTreeInspectionException or OperationCanceledException or Win32Exception or
        InvalidOperationException or NotSupportedException or AggregateException;
}
