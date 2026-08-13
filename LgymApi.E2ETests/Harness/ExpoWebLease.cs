using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed class ExpoWebLease : IAsyncDisposable
{
    internal const string StartupFailureMessage = "E2E Expo web startup failed.";
    internal const string PortOccupiedMessage = "E2E Expo web port 8083 is already occupied.";
    private const int Port = 8083;
    private readonly IExpoWebProcess _process;
    private readonly object _sync = new();
    private Task? _cleanup;

    private ExpoWebLease(IExpoWebProcess process, bool browserSuppressed, ExpoWebIdentity identity)
    {
        _process = process;
        BrowserSuppressed = browserSuppressed;
        Identity = identity;
    }

    internal Uri BaseUri { get; } = new("http://localhost:8083/");

    internal ExpoWebCleanupReceipt? CleanupReceipt { get; private set; }

    internal bool BrowserSuppressed { get; }

    internal ExpoWebIdentity Identity { get; }

    internal bool PortWasAvailableBeforeStart { get; private set; }

    internal static async Task<ExpoWebLease> StartAsync(
        ExpoWebStartRequest request,
        ExpoWebDependencies? dependencies = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ScenarioApiBaseUri.IsAbsoluteUri)
        {
            throw new InvalidOperationException(StartupFailureMessage);
        }

        dependencies ??= ExpoWebDependencies.CreateDefault();
        cancellationToken.ThrowIfCancellationRequested();
        if (!request.Source.IsInstalled)
        {
            throw new ExpoWebStartupException(StartupFailureMessage);
        }
        if (request.Options.Web.Port != Port)
        {
            throw new ExpoWebStartupException(StartupFailureMessage);
        }

        if (dependencies.PortProbe.IsOccupied(Port))
        {
            throw new ExpoWebStartupException(PortOccupiedMessage);
        }

        var options = request.Options;
        var startupTimeout = TimeSpan.FromSeconds(options.Timeouts.WebStartupSeconds);
        var sessionTimeout = TimeSpan.FromSeconds(options.Timeouts.TestSessionSeconds);
        var requestTimeout = TimeSpan.FromSeconds(options.Timeouts.HttpRequestSeconds);
        var shutdownTimeout = TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds);
        if (startupTimeout <= TimeSpan.Zero || sessionTimeout <= TimeSpan.Zero || requestTimeout <= TimeSpan.Zero || shutdownTimeout <= TimeSpan.Zero)
        {
            throw new ExpoWebStartupException(StartupFailureMessage);
        }

        var process = dependencies.ProcessStarter.Start(CreateProcessRequest(request, sessionTimeout, shutdownTimeout), cancellationToken);
        try
        {
            using var startup = new CancellationTokenSource(startupTimeout);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, startup.Token);
            var outcome = await dependencies.ReadinessMonitor.WaitUntilReadyAsync(
                new Uri("http://localhost:8083/"),
                process.Exit,
                new ExpoWebReadinessBounds(requestTimeout, TimeSpan.FromMilliseconds(100)),
                lifetime.Token);
            if (outcome == ExpoWebReadinessOutcome.Ready)
            {
                return new ExpoWebLease(process, CreateEnvironment(request)["BROWSER"] == "none", ExpoWebIdentity.Create())
                {
                    PortWasAvailableBeforeStart = true
                };
            }

            throw new ExpoWebStartupException(StartupFailureMessage, CategoryFor(outcome));
        }
        catch (ExpoWebStartupException startupFailure)
        {
            try
            {
                await process.DisposeAsync();
            }
            catch (Exception)
            {
                throw new ExpoWebStartupException(
                    StartupFailureMessage,
                    startupFailure.Category,
                    cleanupFailed: true);
            }

            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await process.DisposeAsync();
            throw;
        }
        catch (OperationCanceledException)
        {
            await process.DisposeAsync();
            throw new ExpoWebStartupException(StartupFailureMessage);
        }
        catch
        {
            await process.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _cleanup ??= CleanupAsync();
            return new ValueTask(_cleanup);
        }
    }

    private static ExternalProcessRequest CreateProcessRequest(
        ExpoWebStartRequest request,
        TimeSpan startupTimeout,
        TimeSpan shutdownTimeout) => new()
    {
        FileName = request.Source.NodeExecutable,
        Arguments = [request.Source.NpmCliScript, "run", "web"],
        WorkingDirectory = request.Source.SourceDirectory,
        EnvironmentVariables = CreateEnvironment(request),
        ClearEnvironment = true,
        ExecutionTimeout = startupTimeout,
        ShutdownTimeout = shutdownTimeout
        };

    private static Dictionary<string, string?> CreateEnvironment(ExpoWebStartRequest request) =>
        request.RuntimeDirectory is null
            ? request.Source.CreateExpoEnvironment(request.ScenarioApiBaseUri)
            : request.Source.CreateExpoEnvironment(request.ScenarioApiBaseUri, request.RuntimeDirectory);

    private static ExpoWebStartupFailureCategory CategoryFor(ExpoWebReadinessOutcome outcome) => outcome switch
    {
        ExpoWebReadinessOutcome.ProcessExited => ExpoWebStartupFailureCategory.ProcessExit,
        ExpoWebReadinessOutcome.StartupTimeout => ExpoWebStartupFailureCategory.Timeout,
        ExpoWebReadinessOutcome.HttpFailure or ExpoWebReadinessOutcome.HttpTimeout => ExpoWebStartupFailureCategory.Transport,
        _ => ExpoWebStartupFailureCategory.Transport
    };

    private async Task CleanupAsync()
    {
        var receipt = _process.CleanupReceipt;
        try
        {
            await _process.DisposeAsync();
        }
        finally
        {
            receipt = _process.CleanupReceipt ?? receipt;
            CleanupReceipt = new ExpoWebCleanupReceipt(
                receipt?.Cleanup.AllAbsentOrReused ?? false,
                receipt?.DrainCompleted ?? false,
                receipt?.InspectionCompleted ?? false);
        }
    }

    public override string ToString() => "<expo-web-lease>";
}

internal sealed class OwnedExpoWebProcessStarter : IExpoWebProcessStarter
{
    private readonly OwnedExternalProcessStarter _starter = new();

    public IExpoWebProcess Start(ExternalProcessRequest request, CancellationToken cancellationToken) =>
        new OwnedExpoWebProcess(_starter.Start(request, cancellationToken));
}

internal sealed class OwnedExpoWebProcess(OwnedExternalProcessLease lease) : IExpoWebProcess
{
    public Task<ExpoWebProcessExit> Exit { get; } = MapExitAsync(lease.Exit);

    public OwnedExternalProcessCleanupReceipt? CleanupReceipt => lease.CleanupReceipt;

    public ValueTask DisposeAsync() => lease.DisposeAsync();

    private static async Task<ExpoWebProcessExit> MapExitAsync(Task<OwnedExternalProcessExit> exit) =>
        new((await exit).ExitCode);
}
