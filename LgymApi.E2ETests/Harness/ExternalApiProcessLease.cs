namespace LgymApi.E2ETests.Harness;

internal enum ExternalApiProcessExitKind
{
    AddressInUse,
    CorsPolicyRejected,
    PendingMigrations,
    Exited,
    Failed
}

internal sealed record ExternalApiProcessExit(
    ExternalApiProcessExitKind Kind,
    bool HangfireServerStartObserved = false)
{
    public override string ToString() => "<external-api-process-exit>";
}

internal interface IExternalApiProcess : IAsyncDisposable
{
    Task<ExternalApiProcessExit> Exit { get; }

    TimeSpan ExitObservationTimeout { get; }

    bool ProcessTreeAbsent { get; }
}

internal interface IExternalApiProcessStarter
{
    IExternalApiProcess Start(ExternalProcessRequest request);
}

internal sealed class ExternalApiProcessStarter : IExternalApiProcessStarter
{
    private readonly ExternalProcessRunner _runner = new();

    public IExternalApiProcess Start(ExternalProcessRequest request) =>
        new ExternalApiProcessLease(_runner, request);
}

internal sealed class ExternalApiProcessLease : IExternalApiProcess
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task<ExternalProcessResult> _execution;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private IReadOnlyList<ProcessIdentity> _retainedIdentities = [];
    private int _cancellationRequested;
    private int _disposed;

    internal ExternalApiProcessLease(ExternalProcessRunner runner, ExternalProcessRequest request)
    {
        ExitObservationTimeout = request.ShutdownTimeout;
        _execution = runner.RunAsync(request, _lifetime.Token);
        Exit = ObserveExitAsync(_execution);
    }

    public Task<ExternalApiProcessExit> Exit { get; }

    public TimeSpan ExitObservationTimeout { get; }

    internal bool ExactProcessTreeAbsent { get; private set; }

    public bool ProcessTreeAbsent => ExactProcessTreeAbsent;

    public async ValueTask DisposeAsync()
    {
        await _disposeLock.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (_retainedIdentities.Count != 0)
            {
                if (!await RetryProcessTreeCleanupAsync())
                {
                    throw new ExternalProcessCleanupException(CreateRetryFailureReceipt());
                }

                CompleteDisposal();
                return;
            }

            if (Interlocked.Exchange(ref _cancellationRequested, 1) == 0)
            {
                _lifetime.Cancel();
            }

            try
            {
                await _execution;
                ExactProcessTreeAbsent = true;
            }
            catch (ExternalProcessCanceledException exception) when (_lifetime.IsCancellationRequested)
            {
                CaptureCleanupReceipt(exception.Receipt);
            }
            catch (ExternalProcessTimeoutException exception)
            {
                CaptureCleanupReceipt(exception.Receipt);
            }
            catch (ExternalProcessCleanupException exception) when (exception.Receipt is not null)
            {
                CaptureCleanupReceipt(exception.Receipt);
            }
            catch (ExternalProcessPostLaunchException exception)
            {
                CaptureCleanupReceipt(exception.Receipt);
                CompleteDisposal();
                throw;
            }
            catch (InvalidOperationException exception) when (
                string.Equals(exception.Message, ExternalProcessRunner.StartFailureMessage, StringComparison.Ordinal))
            {
                ExactProcessTreeAbsent = true;
            }

            CompleteDisposal();
        }
        finally
        {
            _disposeLock.Release();
        }
    }

    private void CaptureCleanupReceipt(ExternalProcessFailureReceipt receipt)
    {
        ExactProcessTreeAbsent = receipt.Cleanup.AllAbsentOrReused;
        if (ExactProcessTreeAbsent)
        {
            return;
        }

        _retainedIdentities = receipt.Cleanup.CapturedIdentities.ToArray();
        throw new ExternalProcessCleanupException(receipt);
    }

    private async Task<bool> RetryProcessTreeCleanupAsync()
    {
        using var deadline = new CancellationTokenSource(ExitObservationTimeout);
        try
        {
            WindowsProcessTree.TerminateKnownIdentities(_retainedIdentities);
            await WindowsProcessTree.WaitUntilAllAbsentOrReusedAsync(_retainedIdentities, deadline.Token);
            ExactProcessTreeAbsent = WindowsProcessTree.AllAbsentOrReused(_retainedIdentities);
            return ExactProcessTreeAbsent;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or InvalidOperationException or ProcessTreeInspectionException)
        {
            ExactProcessTreeAbsent = WindowsProcessTree.AllAbsentOrReused(_retainedIdentities);
            return ExactProcessTreeAbsent;
        }
    }

    private ExternalProcessFailureReceipt CreateRetryFailureReceipt() => new(
        new ExternalProcessOutput(string.Empty, WasTruncated: false),
        new ExternalProcessOutput(string.Empty, WasTruncated: false),
        new ProcessCleanupReceipt(_retainedIdentities, AllAbsentOrReused: false));

    private void CompleteDisposal()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _lifetime.Dispose();
    }

    private static async Task<ExternalApiProcessExit> ObserveExitAsync(Task<ExternalProcessResult> execution)
    {
        try
        {
            var result = await execution;
            return new ExternalApiProcessExit(
                IsAddressInUse(result)
                    ? ExternalApiProcessExitKind.AddressInUse
                    : IsCorsPolicyRejected(result.StandardOutput, result.StandardError)
                        ? ExternalApiProcessExitKind.CorsPolicyRejected
                        : HasPendingMigrationsFailure(result.StandardOutput, result.StandardError)
                            ? ExternalApiProcessExitKind.PendingMigrations
                        : ExternalApiProcessExitKind.Exited,
                HasHangfireServerStartEvidence(result.StandardOutput, result.StandardError));
        }
        catch (ExternalProcessTimeoutException exception)
        {
            return FromFailureReceipt(exception.Receipt);
        }
        catch (ExternalProcessCanceledException exception)
        {
            return FromFailureReceipt(exception.Receipt);
        }
        catch (ExternalProcessCleanupException exception)
        {
            return exception.Receipt is null
                ? new ExternalApiProcessExit(ExternalApiProcessExitKind.Failed)
                : FromFailureReceipt(exception.Receipt);
        }
        catch (ExternalProcessPostLaunchException exception)
        {
            return FromFailureReceipt(exception.Receipt);
        }
        catch (InvalidOperationException)
        {
            return new ExternalApiProcessExit(ExternalApiProcessExitKind.Failed);
        }
    }

    private static ExternalApiProcessExit FromFailureReceipt(ExternalProcessFailureReceipt receipt) =>
        new(
            IsCorsPolicyRejected(receipt.StandardOutput, receipt.StandardError)
                ? ExternalApiProcessExitKind.CorsPolicyRejected
                : HasPendingMigrationsFailure(receipt.StandardOutput, receipt.StandardError)
                    ? ExternalApiProcessExitKind.PendingMigrations
                : ExternalApiProcessExitKind.Failed,
            HasHangfireServerStartEvidence(receipt.StandardOutput, receipt.StandardError));

    private static bool IsAddressInUse(ExternalProcessResult result) =>
        ContainsAddressInUse(result.StandardOutput.Tail) || ContainsAddressInUse(result.StandardError.Tail);

    private static bool ContainsAddressInUse(string value) =>
        value.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase);

    private static bool IsCorsPolicyRejected(ExternalProcessOutput standardOutput, ExternalProcessOutput standardError) =>
        ContainsCorsPolicyFailure(standardOutput.Tail) || ContainsCorsPolicyFailure(standardError.Tail);

    private static bool ContainsCorsPolicyFailure(string value) =>
        value.Contains("E2E CORS allowed origins configuration is invalid.", StringComparison.Ordinal);

    private static bool HasPendingMigrationsFailure(
        ExternalProcessOutput standardOutput,
        ExternalProcessOutput standardError) =>
        ContainsPendingMigrationsFailure(standardOutput.Tail) || ContainsPendingMigrationsFailure(standardError.Tail);

    private static bool ContainsPendingMigrationsFailure(string value) =>
        value.Contains("Database schema is behind the application model.", StringComparison.Ordinal);

    private static bool HasHangfireServerStartEvidence(
        ExternalProcessOutput standardOutput,
        ExternalProcessOutput standardError) =>
        ContainsHangfireServerEvidence(standardOutput.Tail) || ContainsHangfireServerEvidence(standardError.Tail);

    private static bool ContainsHangfireServerEvidence(string value) =>
        value.Contains("Hangfire Server", StringComparison.OrdinalIgnoreCase);
}
