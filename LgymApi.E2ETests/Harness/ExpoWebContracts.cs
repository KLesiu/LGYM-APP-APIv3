using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Lifecycle;

namespace LgymApi.E2ETests.Harness;

internal enum ExpoWebReadinessOutcome { Ready, HttpFailure, HttpTimeout, ProcessExited, StartupTimeout }

internal enum ExpoWebStartupFailureCategory { Transport, ProcessExit, Timeout, Cleanup }

internal sealed record ExpoWebStartRequest(WebSourceRunLease Source, Uri ScenarioApiBaseUri)
{
    internal E2EOptions Options { get; init; } = new();

    internal LifecycleComponentDirectoryLease? RuntimeDirectory { get; init; }
}

internal sealed class ExpoWebIdentity
{
    private ExpoWebIdentity(string value) => _value = value;

    private readonly string _value;

    internal static ExpoWebIdentity Create() => new(RandomNumberGenerator.GetHexString(32, lowercase: true));

    public override bool Equals(object? obj) => obj is ExpoWebIdentity other && _value == other._value;

    public override int GetHashCode() => _value.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => "<expo-web-identity>";
}

internal sealed record ExpoWebReadinessBounds(TimeSpan HttpRequestTimeout, TimeSpan PollInterval);

internal interface IExpoWebReadinessMonitor
{
    Task<ExpoWebReadinessOutcome> WaitUntilReadyAsync(Uri endpoint, Task<ExpoWebProcessExit> processExit,
        ExpoWebReadinessBounds bounds, CancellationToken cancellationToken);
}

internal interface IExpoWebPortProbe { bool IsOccupied(int port); }

internal interface IExpoWebProcess : IAsyncDisposable
{
    Task<ExpoWebProcessExit> Exit { get; }
    OwnedExternalProcessCleanupReceipt? CleanupReceipt { get; }
}

internal sealed record ExpoWebProcessExit(int ExitCode)
{
    public override string ToString() => "<expo-web-process-exit>";
}

internal interface IExpoWebProcessStarter
{
    IExpoWebProcess Start(ExternalProcessRequest request, CancellationToken cancellationToken);
}

internal sealed record ExpoWebDependencies(IExpoWebProcessStarter ProcessStarter, IExpoWebPortProbe PortProbe,
    IExpoWebReadinessMonitor ReadinessMonitor)
{
    internal static ExpoWebDependencies CreateDefault() => new(new OwnedExpoWebProcessStarter(),
        new LoopbackExpoWebPortProbe(), new ExpoWebReadinessMonitor());
}

internal sealed record ExpoWebCleanupReceipt(bool ProcessTreeAbsent, bool DrainsCompleted, bool InspectionCompleted)
{
    public override string ToString() => "<expo-web-cleanup>";
}

internal sealed class ExpoWebStartupException(
    string message,
    ExpoWebStartupFailureCategory category = ExpoWebStartupFailureCategory.Transport,
    bool cleanupFailed = false) : InvalidOperationException(message)
{
    internal ExpoWebStartupFailureCategory Category { get; } = category;

    internal bool CleanupFailed { get; } = cleanupFailed;
}

internal sealed class LoopbackExpoWebPortProbe : IExpoWebPortProbe
{
    public bool IsOccupied(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try { listener.Start(); return false; }
        catch (SocketException) { return true; }
        finally { listener.Stop(); }
    }
}
