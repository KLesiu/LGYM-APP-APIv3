using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ExternalProcessTreeRaceTests
{
    private const string BlockingTreeScript = """
        $childScript = '$gate=[System.Threading.ManualResetEventSlim]::new($false);$gate.Wait()'
        $null = Start-Process -FilePath 'pwsh' -ArgumentList @('-NoProfile','-NonInteractive','-Command',$childScript) -NoNewWindow
        [Console]::Out.WriteLine('ready')
        [Console]::Out.Flush()
        $gate = [System.Threading.ManualResetEventSlim]::new($false)
        $gate.Wait()
        """;

    [Test]
    public async Task ExternalProcess_snapshot_discovery_uses_known_identity_after_root_exit()
    {
        using var process = StartBlockingTree();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        IReadOnlyList<ProcessIdentity> capturedBeforeKill = [];
        IReadOnlyList<ProcessIdentity> capturedAfterKill = [];

        try
        {
            var readiness = await process.StandardOutput.ReadLineAsync(deadline.Token);
            Assert.That(readiness, Is.EqualTo("ready"));
            capturedBeforeKill = WindowsProcessTree.Capture(process, deadline.Token);
            Assert.That(capturedBeforeKill.Count, Is.GreaterThanOrEqualTo(2));

            process.Kill(entireProcessTree: false);
            await process.WaitForExitAsync(deadline.Token);
            process.Close();

            var exitedRootCaptureFailed = false;
            try
            {
                _ = WindowsProcessTree.Capture(process, deadline.Token);
            }
            catch (InvalidOperationException)
            {
                exitedRootCaptureFailed = true;
            }

            Assert.That(exitedRootCaptureFailed, Is.True);
            capturedAfterKill = WindowsProcessTree.CaptureFromKnownRoots(capturedBeforeKill, deadline.Token);
            Assert.That(capturedAfterKill.Count, Is.GreaterThanOrEqualTo(2));
            var unprovenRootCapture = WindowsProcessTree.CaptureFromKnownRoots(
                [capturedBeforeKill[0]],
                deadline.Token);
            Assert.That(unprovenRootCapture, Is.EqualTo(new[] { capturedBeforeKill[0] }));
        }
        finally
        {
            using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var allIdentities = capturedBeforeKill
                .Concat(capturedAfterKill)
                .DistinctBy(identity => (identity.ProcessId, identity.StartTimeUtc))
                .ToArray();
            TerminateKnownIdentities(allIdentities);
            await WindowsProcessTree.WaitUntilAllAbsentOrReusedAsync(allIdentities, cleanupDeadline.Token);
        }

        Assert.That(
            WindowsProcessTree.AllAbsentOrReused(capturedBeforeKill.Concat(capturedAfterKill).ToArray()),
            Is.True);
    }

    [Test]
    public async Task ExternalProcess_reused_parent_identity_does_not_capture_unrelated_child()
    {
        using var process = StartBlockingTree();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        IReadOnlyList<ProcessIdentity> actualIdentities = [];

        try
        {
            var readiness = await process.StandardOutput.ReadLineAsync(deadline.Token);
            Assert.That(readiness, Is.EqualTo("ready"));
            actualIdentities = WindowsProcessTree.Capture(process, deadline.Token);
            Assert.That(actualIdentities.Count, Is.GreaterThanOrEqualTo(2));
            var reusedRoot = new ProcessIdentity(
                actualIdentities[0].ProcessId,
                actualIdentities[0].StartTimeUtc.AddMinutes(-1));

            Assert.Throws<ProcessTreeInspectionException>(() =>
                WindowsProcessTree.CaptureFromKnownRoots([reusedRoot], deadline.Token));
        }
        finally
        {
            TerminateKnownIdentities(actualIdentities);
            await WindowsProcessTree.WaitUntilAllAbsentOrReusedAsync(actualIdentities, deadline.Token);
        }
    }

    private static Process StartBlockingTree()
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(BlockingTreeScript);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the deterministic process-tree race fixture.");
    }

    private static void TerminateKnownIdentities(IReadOnlyList<ProcessIdentity> identities)
    {
        foreach (var identity in identities)
        {
            try
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                if (process.StartTime.ToUniversalTime() == identity.StartTimeUtc)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                continue;
            }
        }
    }
}
