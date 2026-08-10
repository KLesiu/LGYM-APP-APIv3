using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
public sealed class DockerContainerProbeTests
{
    [Test]
    public void Caller_cancellation_terminates_and_reaps_the_probe_process()
    {
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        AssertCancellationReapsProcess(
            callerCancellation.Token,
            callerCancellation.Token,
            typeof(OperationCanceledException));
    }

    [Test]
    public void Timeout_cancellation_terminates_and_reaps_the_probe_process()
    {
        using var timeoutCancellation = new CancellationTokenSource();
        timeoutCancellation.Cancel();

        AssertCancellationReapsProcess(
            timeoutCancellation.Token,
            CancellationToken.None,
            typeof(InvalidOperationException));
    }

    [Test]
    public void Inspection_timeout_terminates_and_reaps_the_probe_process()
    {
        using var timeoutCancellation = new CancellationTokenSource();
        timeoutCancellation.Cancel();
        var processId = 0;

        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await DockerContainerProbe.IsAbsentAsync(
                    timeoutCancellation.Token,
                    () =>
                    {
                        var process = StartBlockingProcess();
                        processId = process.Id;
                        return process;
                    }));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo("Docker inspection exceeded the configured shutdown timeout."));
                Assert.That(IsProcessRunning(processId), Is.False, "The timed-out Docker inspection process must be reaped before the timeout propagates.");
            });
        }
        finally
        {
            KillIfRunning(processId);
        }
    }

    private static void AssertCancellationReapsProcess(
        CancellationToken timeoutToken,
        CancellationToken callerToken,
        Type expectedExceptionType)
    {
        var processId = 0;

        try
        {
            Assert.ThrowsAsync(
                expectedExceptionType,
                async () => await DockerContainerProbe.EnsureAvailableAsync(
                    timeoutToken,
                    callerToken,
                    () =>
                    {
                        var process = StartBlockingProcess();
                        processId = process.Id;
                        return process;
                    }));

            Assert.That(IsProcessRunning(processId), Is.False, "The canceled Docker probe process must be reaped before cancellation propagates.");
        }
        finally
        {
            KillIfRunning(processId);
        }
    }

    private static Process StartBlockingProcess()
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("$gate = [System.Threading.ManualResetEventSlim]::new($false); $gate.Wait()");

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the deterministic cancellation fixture process.");
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void KillIfRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch (ArgumentException)
        {
            return;
        }
    }
}
