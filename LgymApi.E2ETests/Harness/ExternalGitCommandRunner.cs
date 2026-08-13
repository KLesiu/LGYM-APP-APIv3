using System.ComponentModel;
using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

internal sealed class ExternalGitCommandRunner : IExternalGitCommandRunner
{
    internal const string CommandFailureMessage = "The read-only Git command failed.";
    internal const string TimeoutMessage = "The read-only Git command exceeded its execution timeout.";
    internal const string CleanupFailureMessage = "The read-only Git process could not be stopped safely.";
    private readonly string _gitExecutable;

    internal ExternalGitCommandRunner(string gitExecutable)
    {
        if (!Path.IsPathFullyQualified(gitExecutable) ||
            !string.Equals(Path.GetFileName(gitExecutable), "git.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(gitExecutable))
        {
            throw new InvalidOperationException(CommandFailureMessage);
        }

        _gitExecutable = Path.GetFullPath(gitExecutable);
    }

    public async Task<ExternalGitCommandResult<T>> RunAsync<T>(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        Func<Stream, CancellationToken, Task<T>> readStandardOutput,
        ExternalGitCommandTimeouts timeouts,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(workingDirectory, timeouts);
        cancellationToken.ThrowIfCancellationRequested();
        await using var privateRun = PrivateRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            RepositoryRoot.Find(),
            ".e2e-private/runs",
            timeouts.Shutdown));
        using var process = Start(CreateStartInfo(workingDirectory, arguments, privateRun));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(timeouts.Execution);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var outputTask = readStandardOutput(process.StandardOutput.BaseStream, CancellationToken.None);
        var errorTask = DrainAsync(process.StandardError.BaseStream, CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(execution.Token);
            await Task.WhenAll(outputTask, errorTask).WaitAsync(execution.Token);
            return new ExternalGitCommandResult<T>(process.ExitCode, await outputTask);
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested)
        {
            await StopAsync(process, outputTask, errorTask, timeouts.Shutdown);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "The read-only Git command was canceled by the caller.",
                    null,
                    cancellationToken);
            }

            throw new InvalidOperationException(TimeoutMessage);
        }
        catch
        {
            await StopAsync(process, outputTask, errorTask, timeouts.Shutdown);
            throw;
        }
    }

    internal static async Task<byte[]> ReadBoundedBytesAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        var overflow = false;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + bytesRead <= maximumBytes)
            {
                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
            else
            {
                overflow = true;
            }
        }

        if (overflow)
        {
            throw new InvalidOperationException(CommandFailureMessage);
        }

        return output.ToArray();
    }

    internal static async Task<bool> DiscardAsync(Stream stream, CancellationToken cancellationToken)
    {
        await DrainAsync(stream, cancellationToken);
        return true;
    }

    internal ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        PrivateRunDirectoryLease privateRun)
    {
        var startInfo = new ProcessStartInfo(_gitExecutable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--no-optional-locks");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.fsmonitor=false");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.untrackedCache=false");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        CreatePrivateEnvironment(startInfo.Environment, privateRun);
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
        startInfo.Environment["LC_ALL"] = "C";
        return startInfo;
    }

    private static Process Start(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException(CommandFailureMessage);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(CommandFailureMessage);
        }
    }

    private static async Task StopAsync(
        Process process,
        Task outputTask,
        Task errorTask,
        TimeSpan shutdownTimeout)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            using var shutdown = new CancellationTokenSource(shutdownTimeout);
            await process.WaitForExitAsync(shutdown.Token);
            await ObserveDrainAsync(outputTask, shutdown.Token);
            await ObserveDrainAsync(errorTask, shutdown.Token);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            throw new InvalidOperationException(CleanupFailureMessage);
        }
    }

    private static async Task ObserveDrainAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private static async Task DrainAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (await stream.ReadAsync(buffer, cancellationToken) != 0)
        {
        }
    }

    private static void ValidateRequest(string workingDirectory, ExternalGitCommandTimeouts timeouts)
    {
        if (!Path.IsPathFullyQualified(workingDirectory) ||
            !Directory.Exists(workingDirectory) ||
            timeouts.Execution <= TimeSpan.Zero ||
            timeouts.Shutdown <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(CommandFailureMessage);
        }
    }

    private void CreatePrivateEnvironment(
        IDictionary<string, string?> environment,
        PrivateRunDirectoryLease privateRun)
    {
        var homeDirectory = Path.Combine(privateRun.RunDirectory, "git-home");
        var temporaryDirectory = Path.Combine(privateRun.RunDirectory, "git-temp");
        Directory.CreateDirectory(homeDirectory);
        Directory.CreateDirectory(temporaryDirectory);
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? string.Empty;
        environment["SystemRoot"] = systemRoot;
        environment["WINDIR"] = Environment.GetEnvironmentVariable("WINDIR") ?? systemRoot;
        environment["ComSpec"] = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(systemRoot, "System32", "cmd.exe");
        environment["HOME"] = homeDirectory;
        environment["USERPROFILE"] = homeDirectory;
        environment["TEMP"] = temporaryDirectory;
        environment["TMP"] = temporaryDirectory;
        environment["PATH"] = string.Join(
            Path.PathSeparator,
            Path.GetDirectoryName(_gitExecutable)!,
            Path.Combine(systemRoot, "System32"));
    }
}
