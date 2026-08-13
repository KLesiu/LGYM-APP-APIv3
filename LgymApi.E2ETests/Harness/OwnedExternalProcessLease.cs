using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

internal sealed partial class OwnedExternalProcessLease : IAsyncDisposable
{
    internal const string StartFailureMessage = "The owned external process could not be started.";
    internal const string CallerCancellationMessage = "The owned external process was canceled by the caller.";
    internal const string CleanupFailureMessage =
        "The owned external process could not be completely reaped within the configured shutdown bounds.";
    private static readonly TimeSpan InspectionInterval = TimeSpan.FromMilliseconds(25);
    private readonly object _identitySync = new();
    private readonly object _cleanupSync = new();
    private readonly Process _process;
    private readonly ExternalProcessRequest _request;
    private readonly ProcessParentIdReader _parentProcessIdReader;
    private readonly CancellationToken _callerCancellation;
    private readonly CancellationTokenSource _drainCancellation = new();
    private readonly BoundedSanitizedStreamCapture _standardOutput;
    private readonly BoundedSanitizedStreamCapture _standardError;
    private readonly Task _standardOutputTask;
    private readonly Task _standardErrorTask;
    private readonly Task _drains;
    private readonly ExternalProcessTermination _termination = new();
    private readonly List<ProcessIdentity> _retainedIdentities;
    private Task<OwnedExternalProcessCleanupReceipt>? _cleanup;
    private int _inspectionCompleted = 1;
    private int _drainCompleted = 1;
    private int _resourcesDisposed;

    internal OwnedExternalProcessLease(
        Process process,
        ProcessIdentity rootIdentity,
        ExternalProcessRequest request,
        ProcessParentIdReader parentProcessIdReader,
        Func<BoundedSanitizedStreamCapture, TextReader, CancellationToken, Task> streamDrainer,
        CancellationToken callerCancellation)
    {
        _process = process;
        RootIdentity = rootIdentity;
        _request = request;
        _parentProcessIdReader = parentProcessIdReader;
        _callerCancellation = callerCancellation;
        _retainedIdentities = [rootIdentity];
        _standardOutput = new BoundedSanitizedStreamCapture(request.SecretCanaries);
        _standardError = new BoundedSanitizedStreamCapture(request.SecretCanaries);
        _standardOutputTask = streamDrainer(
            _standardOutput,
            process.StandardOutput,
            _drainCancellation.Token);
        _standardErrorTask = streamDrainer(
            _standardError,
            process.StandardError,
            _drainCancellation.Token);
        _drains = Task.WhenAll(_standardOutputTask, _standardErrorTask);
        Exit = ObserveExitAsync();
    }

    internal ProcessIdentity RootIdentity { get; }

    internal IReadOnlyList<ProcessIdentity> CapturedIdentities
    {
        get
        {
            lock (_identitySync)
            {
                return _retainedIdentities.ToArray();
            }
        }
    }

    internal Task<OwnedExternalProcessExit> Exit { get; }

    internal OwnedExternalProcessCleanupReceipt? CleanupReceipt { get; private set; }

    public async ValueTask DisposeAsync()
    {
        var receipt = await EnsureCleanupAsync();
        try
        {
            await Exit;
        }
        catch (Exception exception) when (
            exception is OwnedExternalProcessCanceledException or OwnedExternalProcessCleanupException)
        {
        }
        finally
        {
            DisposeResources();
        }

        if (!receipt.Cleanup.AllAbsentOrReused || !receipt.DrainCompleted || !receipt.InspectionCompleted)
        {
            throw new OwnedExternalProcessCleanupException(receipt);
        }
    }

    private async Task<OwnedExternalProcessExit> ObserveExitAsync()
    {
        while (true)
        {
            if (_callerCancellation.IsCancellationRequested)
            {
                var receipt = await EnsureCleanupAsync();
                if (!receipt.Cleanup.AllAbsentOrReused)
                {
                    throw new OwnedExternalProcessCleanupException(receipt);
                }

                throw new OwnedExternalProcessCanceledException(receipt, _callerCancellation);
            }

            try
            {
                RefreshRetainedIdentities(_callerCancellation);
            }
            catch (OperationCanceledException) when (_callerCancellation.IsCancellationRequested)
            {
                var receipt = await EnsureCleanupAsync();
                if (!receipt.Cleanup.AllAbsentOrReused)
                {
                    throw new OwnedExternalProcessCleanupException(receipt);
                }

                throw new OwnedExternalProcessCanceledException(receipt, _callerCancellation);
            }
            catch (Exception exception) when (IsInspectionFailure(exception))
            {
                Interlocked.Exchange(ref _inspectionCompleted, 0);
                var receipt = await EnsureCleanupAsync();
                throw new OwnedExternalProcessCleanupException(receipt);
            }

            if (_process.HasExited)
            {
                var exitCode = _process.ExitCode;
                using var drainDeadline = new CancellationTokenSource(_request.ShutdownTimeout);
                var drainCompleted = await CompleteDrainsWithinBoundAsync(drainDeadline.Token);
                var output = SnapshotOutput();
                return new OwnedExternalProcessExit(
                    exitCode,
                    output.StandardOutput,
                    output.StandardError,
                    CapturedIdentities,
                    drainCompleted,
                    Volatile.Read(ref _inspectionCompleted) == 1);
            }

            try
            {
                await Task.Delay(InspectionInterval, _callerCancellation);
            }
            catch (OperationCanceledException) when (_callerCancellation.IsCancellationRequested)
            {
            }
        }
    }

    private static bool IsInspectionFailure(Exception exception) =>
        exception is ProcessTreeInspectionException or InvalidOperationException;
}
