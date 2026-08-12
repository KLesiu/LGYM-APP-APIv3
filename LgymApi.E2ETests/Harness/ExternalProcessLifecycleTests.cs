using System.Text.Json;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ExternalProcessLifecycleTests
{
    private const string WholeSecret = "timeout-whole-canary-433";
    private const string SplitSecret = "timeout-split-canary-433";

    [Test]
    public async Task ExternalProcess_timeout_reaps_root_and_descendant_before_returning()
    {
        using var fixture = new ExternalProcessFixture();
        var runner = new ExternalProcessRunner();
        var request = fixture.CreateBlockingTreeRequest(WholeSecret, SplitSecret, TimeSpan.FromSeconds(3));
        var runTask = runner.RunAsync(request);
        await fixture.WaitUntilReadyOrFailedAsync(runTask, TimeSpan.FromSeconds(3));

        var exception = Assert.ThrowsAsync<ExternalProcessTimeoutException>(async () => await runTask);
        var receipt = exception!.Receipt;
        var independentlyAbsent = WindowsProcessTree.AllAbsentOrReused(receipt.Cleanup.CapturedIdentities);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo(ExternalProcessRunner.TimeoutMessage));
            Assert.That(receipt.Cleanup.CapturedIdentities.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(receipt.Cleanup.AllAbsentOrReused, Is.True);
            Assert.That(independentlyAbsent, Is.True);
            Assert.That(receipt.StandardOutput.WasTruncated, Is.True);
            Assert.That(receipt.StandardError.WasTruncated, Is.True);
            Assert.That(receipt.StandardOutput.Tail.Contains(WholeSecret, StringComparison.Ordinal), Is.False);
            Assert.That(receipt.StandardOutput.Tail.Contains(SplitSecret, StringComparison.Ordinal), Is.False);
            Assert.That(receipt.StandardError.Tail.Contains(WholeSecret, StringComparison.Ordinal), Is.False);
            Assert.That(receipt.StandardError.Tail.Contains(SplitSecret, StringComparison.Ordinal), Is.False);
        });

        WriteSanitizedEvidence(receipt, independentlyAbsent);
    }

    [Test]
    public async Task ExternalProcess_repeated_caller_cancellation_reaps_the_tree_and_propagates_cancellation()
    {
        using var fixture = new ExternalProcessFixture();
        using var callerCancellation = new CancellationTokenSource();
        var runner = new ExternalProcessRunner();
        var request = fixture.CreateBlockingTreeRequest(WholeSecret, SplitSecret, TimeSpan.FromSeconds(30));
        var runTask = runner.RunAsync(request, callerCancellation.Token);
        await fixture.WaitUntilReadyOrFailedAsync(runTask, TimeSpan.FromSeconds(3));

        callerCancellation.Cancel();
        callerCancellation.Cancel();
        var exception = Assert.ThrowsAsync<ExternalProcessCanceledException>(async () => await runTask);
        var identities = exception!.Receipt.Cleanup.CapturedIdentities;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo(ExternalProcessRunner.CallerCancellationMessage));
            Assert.That(exception.CancellationToken, Is.EqualTo(callerCancellation.Token));
            Assert.That(exception.Receipt.Cleanup.AllAbsentOrReused, Is.True);
            Assert.That(WindowsProcessTree.AllAbsentOrReused(identities), Is.True);
            Assert.That(WindowsProcessTree.AllAbsentOrReused(identities), Is.True);
        });
    }

    private static void WriteSanitizedEvidence(
        ExternalProcessFailureReceipt receipt,
        bool independentlyAbsent)
    {
        var evidenceDirectory = Path.Combine(
            RepositoryRoot.Find(),
            "LgymApi.E2ETests",
            "TestResults",
            "issue-433-task2");
        Directory.CreateDirectory(evidenceDirectory);
        var evidence = new
        {
            capturedIdentityCount = receipt.Cleanup.CapturedIdentities.Count,
            allAbsentOrReused = independentlyAbsent,
            standardOutput = new
            {
                retainedUtf8Bytes = receipt.StandardOutput.RetainedUtf8ByteCount,
                truncated = receipt.StandardOutput.WasTruncated,
                secretCanaryPresent = false
            },
            standardError = new
            {
                retainedUtf8Bytes = receipt.StandardError.RetainedUtf8ByteCount,
                truncated = receipt.StandardError.WasTruncated,
                secretCanaryPresent = false
            }
        };
        File.WriteAllText(
            Path.Combine(evidenceDirectory, "task-2-issue-433-e2e-api-host.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }
}
