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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        try
        {
            await _execution;
            ExactProcessTreeAbsent = true;
        }
        catch (ExternalProcessCanceledException exception) when (_lifetime.IsCancellationRequested)
        {
            ExactProcessTreeAbsent = exception.Receipt.Cleanup.AllAbsentOrReused;
            if (!exception.Receipt.Cleanup.AllAbsentOrReused)
            {
                throw new ExternalProcessCleanupException(exception.Receipt);
            }
        }
        catch (ExternalProcessTimeoutException exception)
        {
            ExactProcessTreeAbsent = exception.Receipt.Cleanup.AllAbsentOrReused;
            if (!exception.Receipt.Cleanup.AllAbsentOrReused)
            {
                throw new ExternalProcessCleanupException(exception.Receipt);
            }
        }
        catch (InvalidOperationException exception) when (exception is not ExternalProcessCleanupException)
        {
            ExactProcessTreeAbsent = true;
        }
        finally
        {
            _lifetime.Dispose();
        }
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
