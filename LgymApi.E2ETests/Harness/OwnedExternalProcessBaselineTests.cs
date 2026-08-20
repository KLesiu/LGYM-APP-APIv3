namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class OwnedExternalProcessBaselineTests
{
    private const string WholeSecret = "owned-baseline-whole-canary-434";
    private const string SplitSecret = "owned-baseline-split-canary-434";

    [Test]
    public async Task OwnedExternalProcess_finite_runner_characterization_times_out_and_reaps_tree()
    {
        using var fixture = new ExternalProcessFixture();
        using var timeoutCancellation = new CancellationTokenSource();
        var runner = new ExternalProcessRunner(
            timeoutCancellationSourceFactory: _ => timeoutCancellation);
        var request = fixture.CreateQuietBlockingTreeRequest(
            WholeSecret,
            SplitSecret,
            TimeSpan.FromMilliseconds(500));
        var runTask = runner.RunAsync(request);
        await fixture.WaitUntilReadyOrFailedAsync(runTask, TimeSpan.FromSeconds(3));
        timeoutCancellation.Cancel();

        var exception = Assert.ThrowsAsync<ExternalProcessTimeoutException>(async () => await runTask);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Receipt.Cleanup.CapturedIdentities.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(exception.Receipt.Cleanup.AllAbsentOrReused, Is.True);
            Assert.That(
                WindowsProcessTree.AllAbsentOrReused(exception.Receipt.Cleanup.CapturedIdentities),
                Is.True);
        });
    }
}
