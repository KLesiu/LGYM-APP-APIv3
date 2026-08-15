using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

internal static class DockerContainerProbe
{
    private const string DockerPrerequisiteMessage = "Docker is unavailable for the E2E PostgreSQL lifecycle. Ensure the Docker daemon is running.";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static Task EnsureAvailableAsync(CancellationToken timeoutToken, CancellationToken callerToken) =>
        EnsureAvailableAsync(timeoutToken, callerToken, StartDockerVersion);

    internal static async Task EnsureAvailableAsync(
        CancellationToken timeoutToken,
        CancellationToken callerToken,
        Func<Process> startProcess)
    {
        using var process = startProcess();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(timeoutToken);
        }
        catch (OperationCanceledException)
        {
            await TerminateAndWaitAsync(process);
            _ = await standardOutputTask;
            _ = await standardErrorTask;

            callerToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(DockerPrerequisiteMessage);
        }

        _ = await standardOutputTask;
        _ = await standardErrorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(DockerPrerequisiteMessage);
        }
    }

    private static async Task TerminateAndWaitAsync(Process process)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                return;
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
    }

    public static async Task<bool> WaitUntilAbsentAsync(string containerId, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        return await WaitUntilAbsentAsync(containerId, cancellation.Token);
    }

    internal static async Task<bool> WaitUntilAbsentAsync(string containerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (true)
        {
            if (await IsAbsentAsync(containerId, cancellationToken))
            {
                return true;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static Task<bool> IsAbsentAsync(string containerId, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("inspect");
        startInfo.ArgumentList.Add("--type");
        startInfo.ArgumentList.Add("container");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{.Id}}");
        startInfo.ArgumentList.Add(containerId);

        return IsAbsentAsync(
            cancellationToken,
            () => Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start Docker inspection for PostgreSQL cleanup."));
    }

    internal static async Task<bool> IsAbsentAsync(
        CancellationToken cancellationToken,
        Func<Process> startProcess)
    {
        using var process = startProcess();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await TerminateAndWaitAsync(process);
            _ = await standardOutputTask;
            _ = await standardErrorTask;

            throw new InvalidOperationException("Docker inspection exceeded the configured shutdown timeout.");
        }

        _ = await standardOutputTask;
        var standardError = await standardErrorTask;
        if (process.ExitCode == 0)
        {
            return false;
        }

        if (standardError.Contains("No such object", StringComparison.OrdinalIgnoreCase) ||
            standardError.Contains("No such container", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new InvalidOperationException($"Docker inspection failed with exit code {process.ExitCode} during PostgreSQL cleanup.");
    }

    private static Process StartDockerVersion()
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("version");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{.Server.Version}}");

        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException(DockerPrerequisiteMessage);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(DockerPrerequisiteMessage);
        }
    }
}
