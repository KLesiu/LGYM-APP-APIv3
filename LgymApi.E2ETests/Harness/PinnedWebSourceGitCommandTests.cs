using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class PinnedWebSourceGitCommandTests
{
    [Test]
    public async Task PinnedWebSource_Git_command_uses_a_private_closed_noninteractive_lock_free_environment()
    {
        // Given
        var runner = new ExternalGitCommandRunner(ApiRepositoryStateReader.ResolveGitExecutable());
        var parentCanaries = new Dictionary<string, string?>
        {
            ["HOME"] = "parent-home-canary",
            ["USERPROFILE"] = "parent-profile-canary",
            ["TEMP"] = "parent-temp-canary",
            ["TMP"] = "parent-tmp-canary",
            ["PATH"] = "parent-path-canary",
            ["LGYM_GIT_PARENT_CANARY"] = "parent-lgym-canary",
            ["GIT_ASKPASS"] = "parent-credential-canary"
        };
        await using var privateRun = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(), ".e2e-private/runs", TimeSpan.FromSeconds(5)));

        // When
        ProcessStartInfo? startInfo = null;
        EnvironmentVariableScope.Run(parentCanaries, () =>
            startInfo = runner.CreateStartInfo(RepositoryRoot.Find(), ["rev-parse", "HEAD"], privateRun));

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(startInfo, Is.Not.Null);
            Assert.That(startInfo!.ArgumentList[0], Is.EqualTo("--no-optional-locks"));
            Assert.That(startInfo.ArgumentList, Does.Contain("core.fsmonitor=false"));
            Assert.That(startInfo.ArgumentList, Does.Contain("core.untrackedCache=false"));
            Assert.That(startInfo.Environment["GIT_OPTIONAL_LOCKS"], Is.EqualTo("0"));
            Assert.That(startInfo.Environment["GIT_TERMINAL_PROMPT"], Is.EqualTo("0"));
            Assert.That(startInfo.Environment["GIT_CONFIG_NOSYSTEM"], Is.EqualTo("1"));
            Assert.That(startInfo.Environment["GIT_CONFIG_GLOBAL"], Is.EqualTo("NUL"));
            Assert.That(startInfo.Environment["LC_ALL"], Is.EqualTo("C"));
            Assert.That(startInfo.Environment.Keys, Is.EqualTo(new[]
            {
                "SystemRoot",
                "WINDIR",
                "ComSpec",
                "HOME",
                "USERPROFILE",
                "TEMP",
                "TMP",
                "PATH",
                "GIT_OPTIONAL_LOCKS",
                "GIT_TERMINAL_PROMPT",
                "GIT_CONFIG_NOSYSTEM",
                "GIT_CONFIG_GLOBAL",
                "LC_ALL"
            }));
            Assert.That(startInfo.Environment["HOME"], Is.EqualTo(startInfo.Environment["USERPROFILE"]));
            Assert.That(startInfo.Environment["TEMP"], Is.EqualTo(startInfo.Environment["TMP"]));
            Assert.That(startInfo.Environment["HOME"], Does.StartWith(privateRun.RunDirectory));
            Assert.That(startInfo.Environment["TEMP"], Does.StartWith(privateRun.RunDirectory));
            Assert.That(startInfo.Environment["HOME"], Is.Not.EqualTo(parentCanaries["HOME"]));
            Assert.That(startInfo.Environment["TEMP"], Is.Not.EqualTo(parentCanaries["TEMP"]));
            Assert.That(startInfo.Environment["PATH"], Is.EqualTo(string.Join(
                Path.PathSeparator,
                Path.GetDirectoryName(ApiRepositoryStateReader.ResolveGitExecutable())!,
                Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "System32"))));
            Assert.That(startInfo.Environment["ComSpec"], Is.Not.Empty);
            Assert.That(startInfo.Environment, Does.Not.ContainKey("LGYM_GIT_PARENT_CANARY"));
            Assert.That(startInfo.Environment, Does.Not.ContainKey("GIT_ASKPASS"));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.RedirectStandardInput, Is.True);
            Assert.That(startInfo.RedirectStandardOutput, Is.True);
            Assert.That(startInfo.RedirectStandardError, Is.True);
        });
    }

    [Test]
    public async Task PinnedWebSource_Git_command_honors_caller_cancellation_before_start()
    {
        // Given
        var runner = new ExternalGitCommandRunner(ApiRepositoryStateReader.ResolveGitExecutable());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // When
        var exception = Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await runner.RunAsync(
                RepositoryRoot.Find(),
                ["rev-parse", "HEAD"],
                (stream, token) => ExternalGitCommandRunner.ReadBoundedBytesAsync(stream, 1024, token),
                new ExternalGitCommandTimeouts(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
                cancellation.Token));

        // Then
        Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public void PinnedWebSource_Git_command_stops_repeated_reads_when_execution_deadline_elapses()
    {
        // Given
        var runner = new ExternalGitCommandRunner(ApiRepositoryStateReader.ResolveGitExecutable());

        for (var attempt = 0; attempt < 2; attempt++)
        {
            // When
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runner.RunAsync(
                    RepositoryRoot.Find(),
                    ["status", "--porcelain=v1", "-z", "--untracked-files=all"],
                    (stream, token) => ExternalGitCommandRunner.ReadBoundedBytesAsync(stream, 1024, token),
                    new ExternalGitCommandTimeouts(TimeSpan.FromTicks(1), TimeSpan.FromSeconds(5))));

            // Then
            Assert.That(exception!.Message, Is.EqualTo(ExternalGitCommandRunner.TimeoutMessage));
        }
    }

}
