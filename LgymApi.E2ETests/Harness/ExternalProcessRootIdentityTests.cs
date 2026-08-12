using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ExternalProcessRootIdentityTests
{
    private const string WholeSecret = "root-identity-whole-canary-433";
    private const string SplitSecret = "root-identity-split-canary-433";

    [Test]
    public async Task ExternalProcess_cancellation_uses_retained_identity_after_root_exit()
    {
        using var fixture = new ExternalProcessFixture();
        using var callerCancellation = new CancellationTokenSource();
        var hookInvoked = false;
        var identityCaptureCount = 0;
        var runner = new ExternalProcessRunner(
            rootIdentityFactory: process =>
            {
                identityCaptureCount++;
                return new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime());
            },
            beforeCancellationCleanup: async (process, timeout) =>
            {
                hookInvoked = true;
                using var deadline = new CancellationTokenSource(timeout);
                process.Kill(entireProcessTree: false);
                await process.WaitForExitAsync(deadline.Token);
                process.Close();
            });
        var request = fixture.CreateQuietBlockingTreeRequest(
            WholeSecret,
            SplitSecret,
            TimeSpan.FromSeconds(30));
        var runTask = runner.RunAsync(request, callerCancellation.Token);
        await fixture.WaitUntilReadyOrFailedAsync(runTask, TimeSpan.FromSeconds(3));

        callerCancellation.Cancel();
        var exception = Assert.ThrowsAsync<ExternalProcessCanceledException>(async () => await runTask);
        var identities = exception!.Receipt.Cleanup.CapturedIdentities;

        Assert.Multiple(() =>
        {
            Assert.That(hookInvoked, Is.True);
            Assert.That(identityCaptureCount, Is.EqualTo(1));
            Assert.That(identities.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(exception.Receipt.Cleanup.AllAbsentOrReused, Is.True);
            Assert.That(WindowsProcessTree.AllAbsentOrReused(identities), Is.True);
        });
    }

    [Test]
    public async Task ExternalProcess_immediate_root_identity_failure_cleans_real_tree()
    {
        using var fixture = new ExternalProcessFixture();
        IReadOnlyList<ProcessIdentity> observedIdentities = [];
        var runner = new ExternalProcessRunner(rootIdentityFactory: process =>
        {
            fixture.WaitUntilReadyAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            using var captureDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            observedIdentities = WindowsProcessTree.Capture(process, captureDeadline.Token);
            throw new InvalidOperationException();
        });
        var request = fixture.CreateQuietBlockingTreeRequest(
            WholeSecret,
            SplitSecret,
            TimeSpan.FromSeconds(5));

        var exception = Assert.ThrowsAsync<ExternalProcessCleanupException>(
            async () => await runner.RunAsync(request));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalProcessRunner.CleanupFailureMessage));
            Assert.That(exception.Receipt, Is.Not.Null);
            Assert.That(exception.Receipt!.Cleanup.AllAbsentOrReused, Is.False);
            Assert.That(observedIdentities.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(WindowsProcessTree.AllAbsentOrReused(observedIdentities), Is.True);
        });
    }
}
