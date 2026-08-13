using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class OwnedExternalProcessLeaseFailureTests
{
    private const string WholeSecret = "owned-failure-whole-canary-434";
    private const string SplitSecret = "owned-failure-split-canary-434";

    [Test]
    public async Task OwnedExternalProcess_readiness_exception_still_awaits_owned_tree_cleanup()
    {
        using var fixture = new OwnedExternalProcessFixture();
        using var deadline = OwnedExternalProcessTestSupport.CreateDeadline();
        var lease = new OwnedExternalProcessStarter().Start(
            fixture.CreateBlockingTreeRequest(WholeSecret, SplitSecret));

        try
        {
            await fixture.WaitUntilReadyAsync(lease.Exit, deadline.Token);
            throw new ExpectedReadinessException();
        }
        catch (ExpectedReadinessException)
        {
        }
        finally
        {
            await OwnedExternalProcessTestSupport.DisposeAsync(lease, deadline.Token);
        }

        var receipt = lease.CleanupReceipt!;
        Assert.That(receipt.Cleanup.AllAbsentOrReused, Is.True);
        OwnedExternalProcessTestSupport.AssertIdentityFacts(
            lease.RootIdentity,
            receipt.Cleanup.CapturedIdentities,
            expectAbsent: true);
    }

    [Test]
    public async Task OwnedExternalProcess_root_exit_with_inherited_stream_is_bounded_and_reaps_retained_child()
    {
        using var fixture = new OwnedExternalProcessFixture();
        using var deadline = OwnedExternalProcessTestSupport.CreateDeadline();
        var lease = new OwnedExternalProcessStarter().Start(fixture.CreateRootExitWithChildRequest());
        OwnedExternalProcessCleanupException exception;
        OwnedExternalProcessExit exit;
        try
        {
            await fixture.WaitUntilReadyAsync(lease.Exit, deadline.Token);
            exit = await lease.Exit.WaitAsync(deadline.Token);
            exception = await OwnedExternalProcessTestSupport.CaptureAsync<OwnedExternalProcessCleanupException>(
                () => lease.DisposeAsync().AsTask(),
                deadline.Token);
        }
        finally
        {
            await OwnedExternalProcessTestSupport.DisposeIfNeededAsync(lease, deadline.Token);
        }

        Assert.Multiple(() =>
        {
            Assert.That(exit.ExitCode, Is.EqualTo(7));
            Assert.That(exception.Receipt.DrainCompleted, Is.False);
            Assert.That(exception.Receipt.Cleanup.AllAbsentOrReused, Is.True);
        });
        OwnedExternalProcessTestSupport.AssertIdentityFacts(
            lease.RootIdentity,
            exception.Receipt.Cleanup.CapturedIdentities,
            expectAbsent: true);
    }

    [Test]
    public async Task OwnedExternalProcess_inspection_fault_is_sanitized_after_exact_tree_cleanup()
    {
        using var fixture = new OwnedExternalProcessFixture();
        using var deadline = OwnedExternalProcessTestSupport.CreateDeadline();
        var failInspection = false;
        var reader = ProcessParentIdReader.CreateRuntime(_ =>
            failInspection ? new InvalidOperationException("inspection-canary") : null);
        var lease = new OwnedExternalProcessStarter(parentProcessIdReader: reader).Start(
            fixture.CreateBlockingTreeRequest(WholeSecret, SplitSecret));
        OwnedExternalProcessCleanupException exception;
        try
        {
            await fixture.WaitUntilReadyAsync(lease.Exit, deadline.Token);
            failInspection = true;
            exception = await OwnedExternalProcessTestSupport.CaptureAsync<OwnedExternalProcessCleanupException>(
                () => lease.DisposeAsync().AsTask(),
                deadline.Token);
        }
        finally
        {
            await OwnedExternalProcessTestSupport.DisposeIfNeededAsync(lease, deadline.Token);
        }

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo(OwnedExternalProcessLease.CleanupFailureMessage));
            Assert.That(exception.Message, Does.Not.Contain("inspection-canary"));
            Assert.That(exception.Receipt.InspectionCompleted, Is.False);
            Assert.That(exception.Receipt.Cleanup.AllAbsentOrReused, Is.True);
        });
        OwnedExternalProcessTestSupport.AssertIdentityFacts(
            lease.RootIdentity,
            exception.Receipt.Cleanup.CapturedIdentities,
            expectAbsent: true);
    }

    [Test]
    public async Task OwnedExternalProcess_late_drain_fault_is_sanitized_after_exact_tree_cleanup()
    {
        using var fixture = new OwnedExternalProcessFixture();
        using var deadline = OwnedExternalProcessTestSupport.CreateDeadline();
        var drainNumber = 0;
        var starter = new OwnedExternalProcessStarter(streamDrainer: async (capture, reader, cancellationToken) =>
        {
            var failAfterDrain = Interlocked.Increment(ref drainNumber) == 1;
            await capture.DrainAsync(reader, cancellationToken);
            if (failAfterDrain)
            {
                throw new IOException("drain-canary");
            }
        });
        var lease = starter.Start(fixture.CreateBlockingTreeRequest(WholeSecret, SplitSecret));
        OwnedExternalProcessCleanupException exception;
        try
        {
            await fixture.WaitUntilReadyAsync(lease.Exit, deadline.Token);
            exception = await OwnedExternalProcessTestSupport.CaptureAsync<OwnedExternalProcessCleanupException>(
                () => lease.DisposeAsync().AsTask(),
                deadline.Token);
        }
        finally
        {
            await OwnedExternalProcessTestSupport.DisposeIfNeededAsync(lease, deadline.Token);
        }

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo(OwnedExternalProcessLease.CleanupFailureMessage));
            Assert.That(exception.Message, Does.Not.Contain("drain-canary"));
            Assert.That(exception.Receipt.DrainCompleted, Is.False);
            Assert.That(exception.Receipt.Cleanup.AllAbsentOrReused, Is.True);
        });
        OwnedExternalProcessTestSupport.AssertIdentityFacts(
            lease.RootIdentity,
            exception.Receipt.Cleanup.CapturedIdentities,
            expectAbsent: true);
    }

    [Test]
    public async Task OwnedExternalProcess_cleanup_uses_one_total_shutdown_deadline()
    {
        using var fixture = new OwnedExternalProcessFixture();
        using var deadline = OwnedExternalProcessTestSupport.CreateDeadline();
        var releaseLateDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateDrainFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drainNumber = 0;
        var starter = new OwnedExternalProcessStarter(streamDrainer: async (capture, reader, cancellationToken) =>
        {
            var delayAfterDrain = Interlocked.Increment(ref drainNumber) == 1;
            await capture.DrainAsync(reader, cancellationToken);
            if (delayAfterDrain)
            {
                await releaseLateDrain.Task;
                lateDrainFinished.TrySetResult();
            }
        });
        var lease = starter.Start(fixture.CreateBlockingTreeRequest(
            WholeSecret,
            SplitSecret,
            TimeSpan.FromMilliseconds(500)));
        OwnedExternalProcessCleanupException exception;
        var stopwatch = new Stopwatch();
        try
        {
            await fixture.WaitUntilReadyAsync(lease.Exit, deadline.Token);
            stopwatch.Start();
            exception = await OwnedExternalProcessTestSupport.CaptureAsync<OwnedExternalProcessCleanupException>(
                () => lease.DisposeAsync().AsTask(),
                deadline.Token);
        }
        finally
        {
            releaseLateDrain.TrySetResult();
            await lateDrainFinished.Task.WaitAsync(deadline.Token);
            await OwnedExternalProcessTestSupport.DisposeIfNeededAsync(lease, deadline.Token);
        }

        stopwatch.Stop();
        Assert.Multiple(() =>
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(800)));
            Assert.That(exception.Receipt.DrainCompleted, Is.False);
            Assert.That(exception.Receipt.Cleanup.AllAbsentOrReused, Is.True);
        });
        OwnedExternalProcessTestSupport.AssertIdentityFacts(
            lease.RootIdentity,
            exception.Receipt.Cleanup.CapturedIdentities,
            expectAbsent: true);
    }

    private sealed class ExpectedReadinessException : Exception;
}
