namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Lifecycle")]
[Category("ApiHostProof")]
public sealed class ExternalApiProcessLeaseTests
{
    [Test]
    public async Task ApiHostObservation_process_failure_requires_positive_tree_absence_evidence()
    {
        using var fixture = new ExternalProcessFixture();
        IReadOnlyList<ProcessIdentity> capturedIdentities = [];
        var runner = new ExternalProcessRunner(beforeCancellationCleanup: (process, timeout) =>
        {
            using var captureDeadline = new CancellationTokenSource(timeout);
            capturedIdentities = WindowsProcessTree.Capture(process, captureDeadline.Token);
            throw new InvalidOperationException("Injected post-launch process failure.");
        });
        var request = fixture.CreateQuietBlockingTreeRequest(
            "task-3-process-whole-canary",
            "task-3-process-split-canary",
            TimeSpan.FromSeconds(30));
        var lease = new ExternalApiProcessLease(runner, request);
        await fixture.WaitUntilReadyOrFailedAsync(lease.Exit, TimeSpan.FromSeconds(3));

        InvalidOperationException? exception;
        try
        {
            exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await lease.DisposeAsync());
        }
        finally
        {
            if (capturedIdentities.Count != 0)
            {
                WindowsProcessTree.TerminateKnownIdentities(capturedIdentities);
                using var absenceDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await WindowsProcessTree.WaitUntilAllAbsentOrReusedAsync(
                    capturedIdentities,
                    absenceDeadline.Token);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Injected post-launch process failure."));
            Assert.That(lease.ProcessTreeAbsent, Is.False);
            Assert.That(capturedIdentities.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(WindowsProcessTree.AllAbsentOrReused(capturedIdentities), Is.True);
        });
    }
}
