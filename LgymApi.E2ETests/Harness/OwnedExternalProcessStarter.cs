using System.ComponentModel;
using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

internal sealed class OwnedExternalProcessStarter
{
    private readonly Func<bool> _isWindows;
    private readonly ProcessParentIdReader _parentProcessIdReader;
    private readonly Func<BoundedSanitizedStreamCapture, TextReader, CancellationToken, Task> _streamDrainer;

    internal OwnedExternalProcessStarter(
        Func<bool>? isWindows = null,
        ProcessParentIdReader? parentProcessIdReader = null,
        Func<BoundedSanitizedStreamCapture, TextReader, CancellationToken, Task>? streamDrainer = null)
    {
        _isWindows = isWindows ?? OperatingSystem.IsWindows;
        _parentProcessIdReader = parentProcessIdReader ?? ProcessParentIdReader.CreateRuntime();
        _streamDrainer = streamDrainer ?? ((capture, reader, cancellationToken) =>
            capture.DrainAsync(reader, cancellationToken));
    }

    internal OwnedExternalProcessLease Start(
        ExternalProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        var process = StartProcess(request);
        try
        {
            var rootIdentity = new ProcessIdentity(process.Id, process.StartTime.ToUniversalTime());
            return new OwnedExternalProcessLease(
                process,
                rootIdentity,
                request,
                _parentProcessIdReader,
                _streamDrainer,
                cancellationToken);
        }
        catch
        {
            TryStopUnreturnedProcess(process);
            process.Dispose();
            throw new InvalidOperationException(OwnedExternalProcessLease.StartFailureMessage);
        }
    }

    private void EnsureSupported()
    {
        if (!_isWindows())
        {
            throw new PlatformNotSupportedException(ExternalProcessRunner.WindowsPrerequisiteMessage);
        }

        WindowsProcessTree.EnsureSupported(_parentProcessIdReader);
    }

    private static Process StartProcess(ExternalProcessRequest request)
    {
        try
        {
            return Process.Start(ExternalProcessRunner.CreateStartInfo(request))
                ?? throw new InvalidOperationException(OwnedExternalProcessLease.StartFailureMessage);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(OwnedExternalProcessLease.StartFailureMessage);
        }
    }

    private static void TryStopUnreturnedProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }
}
