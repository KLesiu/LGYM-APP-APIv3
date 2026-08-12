namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ExternalProcessOutputTests
{
    private const string WholeSecret = "whole-secret-canary-433";
    private const string SplitSecret = "split-secret-canary-433";

    [Test]
    public async Task ExternalProcess_output_is_concurrently_drained_sanitized_and_bounded()
    {
        using var fixture = new ExternalProcessFixture();
        var runner = new ExternalProcessRunner();

        var result = await runner.RunAsync(fixture.CreateOutputRequest(WholeSecret, SplitSecret));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput.WasTruncated, Is.True);
            Assert.That(result.StandardError.WasTruncated, Is.True);
            Assert.That(result.StandardOutput.RetainedUtf8ByteCount, Is.LessThanOrEqualTo(ExternalProcessOutput.MaximumTailBytes));
            Assert.That(result.StandardError.RetainedUtf8ByteCount, Is.LessThanOrEqualTo(ExternalProcessOutput.MaximumTailBytes));
            Assert.That(result.StandardOutput.Tail.Contains(WholeSecret, StringComparison.Ordinal), Is.False);
            Assert.That(result.StandardOutput.Tail.Contains(SplitSecret, StringComparison.Ordinal), Is.False);
            Assert.That(result.StandardError.Tail.Contains(WholeSecret, StringComparison.Ordinal), Is.False);
            Assert.That(result.StandardError.Tail.Contains(SplitSecret, StringComparison.Ordinal), Is.False);
            Assert.That(result.StandardOutput.Tail, Does.EndWith("::stdout-end::"));
            Assert.That(result.StandardError.Tail, Does.EndWith("::stderr-end::"));
        });
    }

    [Test]
    public void ExternalProcess_redactor_carries_a_secret_split_between_chunks()
    {
        var redactor = new StreamingSecretRedactor([WholeSecret, SplitSecret]);

        var first = redactor.Transform(
            $"prefix-{WholeSecret}-middle-{SplitSecret[..7]}",
            isFinal: false);
        var second = redactor.Transform(
            $"{SplitSecret[7..]}-suffix",
            isFinal: true);
        var sanitized = string.Concat(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(sanitized.Contains(WholeSecret, StringComparison.Ordinal), Is.False);
            Assert.That(sanitized.Contains(SplitSecret, StringComparison.Ordinal), Is.False);
            Assert.That(sanitized.StartsWith("prefix-[REDACTED]-middle-", StringComparison.Ordinal), Is.True);
            Assert.That(sanitized.EndsWith("[REDACTED]-suffix", StringComparison.Ordinal), Is.True);
        });
    }

    [Test]
    public void ExternalProcess_outside_Windows_fails_with_a_stable_sanitized_prerequisite()
    {
        var runner = new ExternalProcessRunner(() => false);
        var request = new ExternalProcessRequest
        {
            FileName = "must-not-start",
            WorkingDirectory = Environment.CurrentDirectory,
            ExecutionTimeout = TimeSpan.FromSeconds(1),
            ShutdownTimeout = TimeSpan.FromSeconds(1)
        };

        var exception = Assert.ThrowsAsync<PlatformNotSupportedException>(async () => await runner.RunAsync(request));

        Assert.That(exception!.Message, Is.EqualTo(ExternalProcessRunner.WindowsPrerequisiteMessage));
    }
}
