using System.Text;

namespace LgymApi.E2ETests.Harness;

internal sealed class ExternalProcessRequest
{
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public required string WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } =
        new Dictionary<string, string?>();

    public bool ClearEnvironment { get; init; }

    public IReadOnlyList<string> SecretCanaries { get; init; } = [];

    public required TimeSpan ExecutionTimeout { get; init; }

    public required TimeSpan ShutdownTimeout { get; init; }
}

internal sealed record ExternalProcessOutput(string Tail, bool WasTruncated)
{
    public const int MaximumTailBytes = 64 * 1024;

    public int RetainedUtf8ByteCount => Encoding.UTF8.GetByteCount(Tail);
}

internal sealed record ExternalProcessResult(
    int ExitCode,
    ExternalProcessOutput StandardOutput,
    ExternalProcessOutput StandardError);

internal sealed class ProcessIdentity(int processId, DateTime startTimeUtc)
{
    internal int ProcessId { get; } = processId;

    internal DateTime StartTimeUtc { get; } = startTimeUtc;

    public override string ToString() => "<captured-process-identity>";
}

internal sealed record ProcessCleanupReceipt(
    IReadOnlyList<ProcessIdentity> CapturedIdentities,
    bool AllAbsentOrReused);

internal sealed record ExternalProcessFailureReceipt(
    ExternalProcessOutput StandardOutput,
    ExternalProcessOutput StandardError,
    ProcessCleanupReceipt Cleanup);

internal sealed class ExternalProcessTimeoutException(ExternalProcessFailureReceipt receipt)
    : InvalidOperationException(ExternalProcessRunner.TimeoutMessage)
{
    public ExternalProcessFailureReceipt Receipt { get; } = receipt;
}

internal sealed class ExternalProcessCanceledException(
    ExternalProcessFailureReceipt receipt,
    CancellationToken cancellationToken)
    : OperationCanceledException(ExternalProcessRunner.CallerCancellationMessage, null, cancellationToken)
{
    public ExternalProcessFailureReceipt Receipt { get; } = receipt;
}

internal sealed class ExternalProcessCleanupException(ExternalProcessFailureReceipt? receipt = null)
    : InvalidOperationException(ExternalProcessRunner.CleanupFailureMessage)
{
    public ExternalProcessFailureReceipt? Receipt { get; } = receipt;
}
