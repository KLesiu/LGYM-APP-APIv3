using System.Reflection;
using System.Text.Json;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ExternalProcessInspectionFailureTests
{
    private const string WholeSecret = "inspection-whole-canary-433";
    private const string SplitSecret = "inspection-split-canary-433";

    [TestCase(ReflectionFailure.Missing)]
    [TestCase(ReflectionFailure.Unreadable)]
    [TestCase(ReflectionFailure.WrongType)]
    [TestCase(ReflectionFailure.NonInt)]
    [TestCase(ReflectionFailure.Throwing)]
    public async Task ExternalProcess_reflection_contract_failure_prevents_launch(
        ReflectionFailure failure)
    {
        using var fixture = new ExternalProcessFixture();
        var runner = new ExternalProcessRunner(
            parentProcessIdReader: CreateInvalidReader(failure));
        var request = fixture.CreateBlockingTreeRequest(
            WholeSecret,
            SplitSecret,
            TimeSpan.FromSeconds(1));

        Exception? caught = null;
        try
        {
            _ = await runner.RunAsync(request);
        }
        catch (Exception exception)
        {
            caught = exception;
        }

        Assert.Multiple(() =>
        {
            Assert.That(caught, Is.TypeOf<PlatformNotSupportedException>());
            Assert.That(caught!.Message, Is.EqualTo(ProcessParentIdReader.PrerequisiteMessage));
            Assert.That(fixture.HasSignaledReady, Is.False);
        });
    }

    [Test]
    public async Task ExternalProcess_post_kill_cleanup_uses_pre_kill_identities_without_rediscovery()
    {
        using var fixture = new ExternalProcessFixture();
        var reader = ProcessParentIdReader.CreateRuntime(snapshotNumber =>
            snapshotNumber == 2 ? new InvalidOperationException() : null);
        var runner = new ExternalProcessRunner(parentProcessIdReader: reader);
        var request = fixture.CreateBlockingTreeRequest(
            WholeSecret,
            SplitSecret,
            TimeSpan.FromSeconds(2));
        var runTask = runner.RunAsync(request);
        await fixture.WaitUntilReadyOrFailedAsync(runTask, TimeSpan.FromSeconds(2));

        var exception = Assert.ThrowsAsync<ExternalProcessTimeoutException>(async () => await runTask);
        var verifiedReceipt = exception!.Receipt;
        var independentlyAbsent = WindowsProcessTree.AllAbsentOrReused(
            verifiedReceipt.Cleanup.CapturedIdentities);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo(ExternalProcessRunner.TimeoutMessage));
            Assert.That(verifiedReceipt.Cleanup.CapturedIdentities.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(verifiedReceipt.Cleanup.AllAbsentOrReused, Is.True);
            Assert.That(independentlyAbsent, Is.True);
            Assert.That(verifiedReceipt.StandardOutput.Tail.Contains(WholeSecret, StringComparison.Ordinal), Is.False);
            Assert.That(verifiedReceipt.StandardError.Tail.Contains(SplitSecret, StringComparison.Ordinal), Is.False);
        });

        WriteSanitizedCleanupEvidence(verifiedReceipt, independentlyAbsent);
    }

    private static ProcessParentIdReader CreateInvalidReader(ReflectionFailure failure) => failure switch
    {
        ReflectionFailure.Missing => new ProcessParentIdReader(null, canRead: false, getter: null),
        ReflectionFailure.Unreadable => new ProcessParentIdReader(typeof(int), canRead: false, _ => 1),
        ReflectionFailure.WrongType => new ProcessParentIdReader(typeof(long), canRead: true, _ => 1L),
        ReflectionFailure.NonInt => new ProcessParentIdReader(typeof(int), canRead: true, _ => "incompatible"),
        ReflectionFailure.Throwing => new ProcessParentIdReader(
            typeof(int),
            canRead: true,
            _ => throw new TargetInvocationException(new InvalidOperationException())),
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };

    private static void WriteSanitizedCleanupEvidence(
        ExternalProcessFailureReceipt receipt,
        bool independentlyAbsent)
    {
        var evidenceDirectory = Path.Combine(
            RepositoryRoot.Find(),
            "LgymApi.E2ETests",
            "TestResults",
            "issue-433-task2-reflection");
        Directory.CreateDirectory(evidenceDirectory);
        var evidence = new
        {
            capturedIdentityCount = receipt.Cleanup.CapturedIdentities.Count,
            exactCleanupClaim = receipt.Cleanup.AllAbsentOrReused,
            independentlyAbsent,
            standardOutputSecretCanaryPresent = false,
            standardErrorSecretCanaryPresent = false
        };
        File.WriteAllText(
            Path.Combine(evidenceDirectory, "post-launch-inspection-cleanup.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    public enum ReflectionFailure
    {
        Missing,
        Unreadable,
        WrongType,
        NonInt,
        Throwing
    }
}
