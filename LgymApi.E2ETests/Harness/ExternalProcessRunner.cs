using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace LgymApi.E2ETests.Harness;

internal sealed class ExternalProcessRunner
{
    internal const string WindowsPrerequisiteMessage = "The external process E2E harness requires Windows.";
    internal const string TimeoutMessage = "The external process exceeded the configured execution timeout.";
    internal const string CallerCancellationMessage = "The external process was canceled by the caller.";
    internal const string CleanupFailureMessage = "The external process could not be proven absent within the configured shutdown timeout.";
    private const string StartFailureMessage = "The external process could not be started.";
    private readonly Func<bool> _isWindows;
    private readonly ExternalProcessTermination _termination;

    internal ExternalProcessRunner(
        Func<bool>? isWindows = null,
        ProcessParentIdReader? parentProcessIdReader = null,
        Func<Process, ProcessIdentity>? rootIdentityFactory = null,
        Func<Process, TimeSpan, Task>? beforeCancellationCleanup = null)
    {
        _isWindows = isWindows ?? OperatingSystem.IsWindows;
        ParentProcessIdReader = parentProcessIdReader ?? ProcessParentIdReader.CreateRuntime();
        _termination = new ExternalProcessTermination();
        RootIdentityFactory = rootIdentityFactory ??
            (process => new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime()));
        BeforeCancellationCleanup = beforeCancellationCleanup;
    }

    internal ProcessParentIdReader ParentProcessIdReader { get; }

    internal Func<Process, ProcessIdentity> RootIdentityFactory { get; }

    internal Func<Process, TimeSpan, Task>? BeforeCancellationCleanup { get; }

    public async Task<ExternalProcessResult> RunAsync(
        ExternalProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Start(request);
        ProcessIdentity rootIdentity;
        try
        {
            rootIdentity = RootIdentityFactory(process);
        }
        catch (Exception)
        {
            using var failedDrainCancellation = new CancellationTokenSource();
            var failedStandardOutput = new BoundedSanitizedStreamCapture(request.SecretCanaries);
            var failedStandardError = new BoundedSanitizedStreamCapture(request.SecretCanaries);
            var failedStandardOutputTask = failedStandardOutput.DrainAsync(
                process.StandardOutput,
                failedDrainCancellation.Token);
            var failedStandardErrorTask = failedStandardError.DrainAsync(
                process.StandardError,
                failedDrainCancellation.Token);
            using var failedShutdownCancellation = new CancellationTokenSource(request.ShutdownTimeout);
            var receipt = await _termination.CompleteUnprovenCleanupAsync(
                process,
                rootIdentity: null,
                failedStandardOutput,
                failedStandardError,
                failedStandardOutputTask,
                failedStandardErrorTask,
                failedDrainCancellation,
                knownIdentities: [],
                failedShutdownCancellation.Token);
            throw new ExternalProcessCleanupException(receipt);
        }

        using var drainCancellation = new CancellationTokenSource();
        var standardOutput = new BoundedSanitizedStreamCapture(request.SecretCanaries);
        var standardError = new BoundedSanitizedStreamCapture(request.SecretCanaries);
        var standardOutputTask = standardOutput.DrainAsync(process.StandardOutput, drainCancellation.Token);
        var standardErrorTask = standardError.DrainAsync(process.StandardError, drainCancellation.Token);

        using var timeoutCancellation = new CancellationTokenSource(request.ExecutionTimeout);
        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        try
        {
            await process.WaitForExitAsync(executionCancellation.Token);
        }
        catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
        {
            var callerCanceled = cancellationToken.IsCancellationRequested;
            using var shutdownCancellation = new CancellationTokenSource(request.ShutdownTimeout);
            var retainedIdentities = WindowsProcessTree.CaptureFromKnownRoots(
                [rootIdentity],
                shutdownCancellation.Token,
                ParentProcessIdReader);
            await InvokeBeforeCancellationCleanupAsync(process, request.ShutdownTimeout);
            var receipt = await _termination.TerminateAsync(
                process,
                rootIdentity,
                retainedIdentities,
                standardOutput,
                standardError,
                standardOutputTask,
                standardErrorTask,
                drainCancellation,
                request.ShutdownTimeout);

            if (callerCanceled)
            {
                throw new ExternalProcessCanceledException(receipt, cancellationToken);
            }

            throw new ExternalProcessTimeoutException(receipt);
        }

        await CompleteDrainsAsync(
            standardOutputTask,
            standardErrorTask,
            drainCancellation,
            request.ShutdownTimeout);
        return new ExternalProcessResult(
            process.ExitCode,
            standardOutput.Snapshot(),
            standardError.Snapshot());
    }

    private void EnsureSupported()
    {
        if (!_isWindows())
        {
            throw new PlatformNotSupportedException(WindowsPrerequisiteMessage);
        }

        WindowsProcessTree.EnsureSupported(ParentProcessIdReader);
    }

    internal static ProcessStartInfo CreateStartInfo(ExternalProcessRequest request)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.ClearEnvironment)
        {
            startInfo.Environment.Clear();
        }

        foreach (var (name, value) in request.EnvironmentVariables)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        return startInfo;
    }

    private static Process Start(ExternalProcessRequest request)
    {
        var startInfo = CreateStartInfo(request);

        try
        {
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException(StartFailureMessage);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(StartFailureMessage);
        }
    }

    private static async Task CompleteDrainsAsync(
        Task standardOutputTask,
        Task standardErrorTask,
        CancellationTokenSource drainCancellation,
        TimeSpan shutdownTimeout)
    {
        using var drainTimeout = new CancellationTokenSource(shutdownTimeout);
        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask).WaitAsync(drainTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            drainCancellation.Cancel();
            throw new ExternalProcessCleanupException();
        }
    }

    private async Task InvokeBeforeCancellationCleanupAsync(
        Process process,
        TimeSpan shutdownTimeout)
    {
        if (BeforeCancellationCleanup is null)
        {
            return;
        }

        using var hookCancellation = new CancellationTokenSource(shutdownTimeout);
        await BeforeCancellationCleanup(process, shutdownTimeout).WaitAsync(hookCancellation.Token);
    }
}
