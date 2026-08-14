using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Lifecycle;

namespace LgymApi.E2ETests.Harness;

internal sealed class ExternalApiHostLease : IAsyncDisposable
{
    internal const string StartupFailureMessage = "External API host startup failed.";
    internal const string StartupTimeoutMessage = "External API host readiness exceeded the configured startup timeout.";
    internal const string AddressInUseMessage = "External API host could not acquire the configured loopback port.";
    internal const string CorsPolicyFailureMessage = "External API host rejected the E2E CORS policy.";
    internal const string PendingMigrationsFailureMessage = "External API host rejected pending migrations.";
    internal const string CallerCancellationMessage = "External API host startup was canceled by the caller.";
    internal const string CleanupFailureMessage = "External API host cleanup failed.";
    private const string CanonicalPrivateRunRoot = ".e2e-private/runs";
    private const int MaximumDynamicPortAttempts = 3;
    private static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromMilliseconds(100);
    private readonly IApiHostDatabaseLease _database;
    private readonly ExternalApiHostInfrastructure _infrastructure;
    private IApiHostRuntimeLease? _runtime;
    private IExternalApiProcess? _process;
    private int _disposed;

    private ExternalApiHostLease(
        IApiHostDatabaseLease database,
        ExternalApiHostInfrastructure infrastructure)
    {
        _database = database;
        _infrastructure = infrastructure;
    }

    internal Uri BaseAddress { get; private set; } = null!;

    internal ExternalApiHostCleanupReceipt CleanupReceipt { get; private set; } =
        new(false, false, false, [], 0);

    internal ScenarioResourceIdentity Identity { get; } = ScenarioResourceIdentity.Create();

    internal bool HangfireServerStartObserved { get; private set; }

    internal ExternalApiHostObservation CreateObservation() =>
        new(Identity, () => CleanupReceipt);

    internal static Task<ExternalApiHostLease> StartAsync(
        ExternalApiHostRequest request,
        CancellationToken cancellationToken = default) =>
        StartAsync(
            new ExternalApiHostCompositionRequest(
                request.Publication,
                new PostgreSqlApiHostDatabaseLease(request.PostgreSql),
                request.Options,
                request.RepositoryRoot)
            {
                EnvironmentName = request.EnvironmentName,
                CorsAllowedOrigins = request.CorsAllowedOrigins
            },
            ExternalApiHostInfrastructure.CreateDefault(),
            cancellationToken);

    internal static Task<ExternalApiHostLease> StartAsync(
        ExternalApiHostCompositionRequest request,
        CancellationToken cancellationToken = default) =>
        StartAsync(request, ExternalApiHostInfrastructure.CreateDefault(), cancellationToken);

    internal static async Task<ExternalApiHostLease> StartAsync(
        ExternalApiHostCompositionRequest request,
        ExternalApiHostInfrastructure infrastructure,
        CancellationToken cancellationToken = default)
    {
        var lease = new ExternalApiHostLease(request.Database, infrastructure);
        using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupTimeout.CancelAfter(TimeSpan.FromSeconds(request.Options.Timeouts.ApiStartupSeconds));

        try
        {
            var shutdownTimeout = TimeSpan.FromSeconds(request.Options.Timeouts.ProcessShutdownSeconds);
            lease._runtime = await infrastructure.RuntimeFactory.CreateAsync(
                new RuntimeConfigurationRequest(
                    new PrivateRunDirectoryRequest(
                        request.RepositoryRoot,
                        CanonicalPrivateRunRoot,
                        shutdownTimeout),
                    new ApiRuntimeDatabase(request.Database.ConnectionString),
                    request.RuntimeProfile)
                {
                    CorsAllowedOrigins = request.CorsAllowedOrigins,
                    ApiRuntimeDirectory = request.ApiRuntimeDirectory
                },
                startupTimeout.Token);
            await lease.StartProcessAsync(request, startupTimeout.Token);
            return lease;
        }
        catch (Exception exception)
        {
            var attemptCleanupFailure = exception as ExternalApiHostCleanupException;
            try
            {
                await lease.DisposeAsync();
            }
            catch (ExternalApiHostCleanupException disposalFailure)
            {
                throw attemptCleanupFailure is null
                    ? disposalFailure
                    : ExternalApiHostCleanup.Merge(attemptCleanupFailure, disposalFailure.Receipt);
            }

            if (attemptCleanupFailure is not null)
            {
                throw ExternalApiHostCleanup.Merge(attemptCleanupFailure, lease.CleanupReceipt);
            }

            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(CallerCancellationMessage, null, cancellationToken);
            }

            if (startupTimeout.IsCancellationRequested)
            {
                throw new ExternalApiHostStartupException(StartupTimeoutMessage);
            }

            if (exception is ExternalApiHostStartupException or ExternalApiHostCleanupException ||
                exception is InvalidOperationException invalidOperation &&
                IsPublicationValidationMessage(invalidOperation.Message))
            {
                throw;
            }

            throw new ExternalApiHostStartupException(StartupFailureMessage);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var result = await ExternalApiHostCleanup.DisposeAsync(_process, _runtime, _database);
        _process = null;
        _runtime = null;
        CleanupReceipt = result.Receipt;
        HangfireServerStartObserved = result.HangfireServerStartObserved;
        if (CleanupReceipt.FailureCount != 0)
        {
            throw new ExternalApiHostCleanupException(CleanupReceipt);
        }
    }

    public override string ToString() => "<external-api-host-lease>";

    private async Task StartProcessAsync(
        ExternalApiHostCompositionRequest request,
        CancellationToken startupToken)
    {
        var dynamicPort = request.Options.Api.Port == 0;
        var maximumAttempts = dynamicPort ? MaximumDynamicPortAttempts : 1;
        var dotNetExecutable = DotNetExecutableResolver.Resolve();
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            startupToken.ThrowIfCancellationRequested();
            var port = dynamicPort
                ? _infrastructure.PortAllocator.Allocate()
                : request.Options.Api.Port;
            var baseAddress = new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
            var processRequest = ExternalApiHostLaunchRequestFactory.Create(
                new ExternalApiHostLaunchRequest(
                    request.Publication,
                    request.Options,
                    dotNetExecutable,
                    _runtime!,
                    baseAddress)
                {
                    EnvironmentName = request.EnvironmentName
                });
            request.Publication.ValidateBeforeLaunch(processRequest);
            _process = _infrastructure.ProcessStarter.Start(processRequest);
            var outcome = await _infrastructure.ReadinessMonitor.WaitUntilReadyAsync(
                new Uri(baseAddress, "health/live"),
                _process.Exit,
                new ApiHostReadinessBounds(
                    TimeSpan.FromSeconds(request.Options.Timeouts.HttpRequestSeconds),
                    ReadinessPollInterval),
                startupToken);
            if (outcome == ApiHostReadinessOutcome.Ready)
            {
                var databaseOutcome = await (_infrastructure.DatabaseReadinessProbe ?? new DatabaseBackedApiReadinessProbe())
                    .WaitUntilReadyAsync(
                    baseAddress,
                    new ApiHostReadinessBounds(
                        TimeSpan.FromSeconds(request.Options.Timeouts.HttpRequestSeconds),
                        ReadinessPollInterval),
                    startupToken);
                if (databaseOutcome == DatabaseBackedApiReadinessOutcome.Ready)
                {
                    BaseAddress = baseAddress;
                    return;
                }
            }

            await StopProcessAttemptAsync();
            if (outcome == ApiHostReadinessOutcome.AddressInUse && dynamicPort && attempt < maximumAttempts)
            {
                continue;
            }

            throw new ExternalApiHostStartupException(outcome == ApiHostReadinessOutcome.AddressInUse
                ? AddressInUseMessage
                : outcome == ApiHostReadinessOutcome.CorsPolicyRejected
                    ? CorsPolicyFailureMessage
                    : outcome == ApiHostReadinessOutcome.PendingMigrations
                        ? PendingMigrationsFailureMessage
                    : outcome == ApiHostReadinessOutcome.StartupTimeout
                    ? StartupTimeoutMessage
                    : StartupFailureMessage);
        }
    }

    private async Task StopProcessAttemptAsync()
    {
        var process = _process;
        _process = null;
        if (process is not null)
        {
            try
            {
                await process.DisposeAsync();
            }
            catch (Exception)
            {
                throw new ExternalApiHostCleanupException(
                    new ExternalApiHostCleanupReceipt(false, false, false, [ExternalApiHostCleanup.ProcessCategory], 1));
            }
        }
    }

    private static bool IsPublicationValidationMessage(string message) =>
        message is ApiPublication.RequiredArtifactMessage or ApiPublication.IntegrityMessage or
            ApiPublication.LaunchCommandMessage;
}
