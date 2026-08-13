namespace LgymApi.E2ETests.Harness;

internal sealed record OwnedExternalProcessExit(
    int ExitCode,
    ExternalProcessOutput StandardOutput,
    ExternalProcessOutput StandardError,
    IReadOnlyList<ProcessIdentity> CapturedIdentities,
    bool DrainCompleted,
    bool InspectionCompleted)
{
    public override string ToString() => "<owned-external-process-exit>";
}

internal sealed record OwnedExternalProcessCleanupReceipt(
    ExternalProcessOutput StandardOutput,
    ExternalProcessOutput StandardError,
    ProcessCleanupReceipt Cleanup,
    bool DrainCompleted,
    bool InspectionCompleted)
{
    public override string ToString() => "<owned-external-process-cleanup>";
}

internal sealed class OwnedExternalProcessCanceledException(
    OwnedExternalProcessCleanupReceipt receipt,
    CancellationToken cancellationToken)
    : OperationCanceledException(
        OwnedExternalProcessLease.CallerCancellationMessage,
        innerException: null,
        cancellationToken)
{
    internal OwnedExternalProcessCleanupReceipt Receipt { get; } = receipt;
}

internal sealed class OwnedExternalProcessCleanupException(OwnedExternalProcessCleanupReceipt receipt)
    : InvalidOperationException(OwnedExternalProcessLease.CleanupFailureMessage)
{
    internal OwnedExternalProcessCleanupReceipt Receipt { get; } = receipt;
}
