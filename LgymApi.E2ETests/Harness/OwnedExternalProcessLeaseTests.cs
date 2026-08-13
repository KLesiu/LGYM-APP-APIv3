using System.Diagnostics;
using System.Text.Json;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class OwnedExternalProcessLeaseTests
{
    private const string WholeSecret = "owned-whole-canary-434";
    private const string SplitSecret = "owned-split-canary-434";

    [Test]
    public async Task OwnedExternalProcess_stays_alive_beyond_readiness_delay_and_disposes_tree_once()
    {
        using var fixture = new OwnedExternalProcessFixture();
        using var deadline = OwnedExternalProcessTestSupport.CreateDeadline();
        var lease = new OwnedExternalProcessStarter().Start(
            fixture.CreateBlockingTreeRequest(WholeSecret, SplitSecret));
        try
        {
            await fixture.WaitUntilReadyAsync(lease.Exit, deadline.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(300), deadline.Token);
            Assert.That(lease.Exit.IsCompleted, Is.False);
            await OwnedExternalProcessTestSupport.DisposeAsync(lease, deadline.Token);
            await OwnedExternalProcessTestSupport.DisposeAsync(lease, deadline.Token);
        }
        finally
        {
            await OwnedExternalProcessTestSupport.DisposeIfNeededAsync(lease, deadline.Token);
        }

        var receipt = lease.CleanupReceipt!;

        Assert.Multiple(() =>
        {
            Assert.That(receipt.Cleanup.CapturedIdentities.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(receipt.Cleanup.AllAbsentOrReused, Is.True);
            Assert.That(receipt.DrainCompleted, Is.True);
            Assert.That(receipt.StandardOutput.WasTruncated, Is.True);
            Assert.That(receipt.StandardError.WasTruncated, Is.True);
            Assert.That(receipt.StandardOutput.Tail, Does.Not.Contain(WholeSecret));
            Assert.That(receipt.StandardOutput.Tail, Does.Not.Contain(SplitSecret));
            Assert.That(receipt.StandardError.Tail, Does.Not.Contain(WholeSecret));
            Assert.That(receipt.StandardError.Tail, Does.Not.Contain(SplitSecret));
        });
        OwnedExternalProcessTestSupport.AssertIdentityFacts(
            lease.RootIdentity,
            receipt.Cleanup.CapturedIdentities,
            expectAbsent: true);

        WriteSanitizedEvidence(receipt);
    }

    [Test]
    public async Task OwnedExternalProcess_observes_generic_early_exit_with_safe_output()
    {
        using var fixture = new OwnedExternalProcessFixture();
        using var deadline = OwnedExternalProcessTestSupport.CreateDeadline();
        var lease = new OwnedExternalProcessStarter().Start(fixture.CreateEarlyExitRequest());
        OwnedExternalProcessExit exit;
        try
        {
            exit = await lease.Exit.WaitAsync(deadline.Token);
        }
        finally
        {
            await OwnedExternalProcessTestSupport.DisposeAsync(lease, deadline.Token);
        }

        Assert.Multiple(() =>
        {
            Assert.That(exit.ExitCode, Is.EqualTo(23));
            Assert.That(exit.StandardOutput.Tail, Does.EndWith("::early-stdout::"));
            Assert.That(exit.StandardError.Tail, Does.EndWith("::early-stderr::"));
        });
        OwnedExternalProcessTestSupport.AssertIdentityFacts(
            lease.RootIdentity,
            exit.CapturedIdentities,
            expectAbsent: true);
    }

    [Test]
    public async Task OwnedExternalProcess_caller_cancellation_reaps_tree_and_remains_idempotent()
    {
        using var fixture = new OwnedExternalProcessFixture();
        using var callerCancellation = new CancellationTokenSource();
        using var deadline = OwnedExternalProcessTestSupport.CreateDeadline();
        var lease = new OwnedExternalProcessStarter().Start(
            fixture.CreateBlockingTreeRequest(WholeSecret, SplitSecret),
            callerCancellation.Token);
        OwnedExternalProcessCanceledException exception;
        try
        {
            await fixture.WaitUntilReadyAsync(lease.Exit, deadline.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
            callerCancellation.Cancel();
            callerCancellation.Cancel();
            exception = await OwnedExternalProcessTestSupport.CaptureAsync<OwnedExternalProcessCanceledException>(
                async () => await lease.Exit,
                deadline.Token);
            await OwnedExternalProcessTestSupport.DisposeAsync(lease, deadline.Token);
            await OwnedExternalProcessTestSupport.DisposeAsync(lease, deadline.Token);
        }
        finally
        {
            await OwnedExternalProcessTestSupport.DisposeIfNeededAsync(lease, deadline.Token);
        }

        Assert.Multiple(() =>
        {
            Assert.That(exception!.CancellationToken, Is.EqualTo(callerCancellation.Token));
            Assert.That(exception.Receipt.Cleanup.AllAbsentOrReused, Is.True);
        });
        OwnedExternalProcessTestSupport.AssertIdentityFacts(
            lease.RootIdentity,
            exception.Receipt.Cleanup.CapturedIdentities,
            expectAbsent: true);
    }

    [Test]
    public async Task OwnedExternalProcess_stale_identity_does_not_terminate_foreign_process()
    {
        using var fixture = new OwnedExternalProcessFixture();
        using var deadline = OwnedExternalProcessTestSupport.CreateDeadline();
        using var foreignProcess = fixture.StartForeignProcess();
        var actualIdentity = new ProcessIdentity(foreignProcess.Id, foreignProcess.StartTime.ToUniversalTime());
        var staleIdentity = new ProcessIdentity(actualIdentity.ProcessId, actualIdentity.StartTimeUtc.AddMinutes(-1));

        try
        {
            WindowsProcessTree.TerminateKnownIdentities([staleIdentity]);
            await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);

            Assert.That(foreignProcess.HasExited, Is.False);
        }
        finally
        {
            WindowsProcessTree.TerminateKnownIdentities([actualIdentity]);
            await WindowsProcessTree.WaitUntilAllAbsentOrReusedAsync([actualIdentity], deadline.Token);
        }
    }

    private static void WriteSanitizedEvidence(OwnedExternalProcessCleanupReceipt receipt)
    {
        var evidenceDirectory = Path.Combine(
            RepositoryRoot.Find(),
            "LgymApi.E2ETests",
            "TestResults",
            "issue-434-task4");
        Directory.CreateDirectory(evidenceDirectory);
        var evidence = new
        {
            capturedIdentityCount = receipt.Cleanup.CapturedIdentities.Count,
            allAbsentOrReused = receipt.Cleanup.AllAbsentOrReused,
            receipt.DrainCompleted,
            receipt.InspectionCompleted,
            standardOutputBounded = receipt.StandardOutput.RetainedUtf8ByteCount <= ExternalProcessOutput.MaximumTailBytes,
            standardErrorBounded = receipt.StandardError.RetainedUtf8ByteCount <= ExternalProcessOutput.MaximumTailBytes,
            secretCanaryPresent = false
        };
        File.WriteAllText(
            Path.Combine(evidenceDirectory, "task-4-issue-434-pinned-expo-playwright-harness.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }
}
