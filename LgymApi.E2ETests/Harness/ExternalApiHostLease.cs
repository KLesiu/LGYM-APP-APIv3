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
    private const string TestingEnvironmentName = "Testing";
    private const int MaximumDynamicPortAttempts = 3;
    private static readonly TimeSpan FailedStartupCleanupRetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromMilliseconds(100);
    private IApiHostDatabaseLease? _database;
    private readonly ExternalApiHostInfrastructure _infrastructure;
    private readonly TimeSpan _cleanupTimeout;
    private readonly SemaphoreSlim _disposeLock = new(1, 1);
    private Task<ExternalApiHostCleanupResult>? _cleanupAttempt;
    private Task<ExternalApiHostCleanupReceipt>? _failedStartupCleanup;
    private IApiHostRuntimeLease? _runtime;
    private IExternalApiProcess? _process;
    private int _disposed;

    private ExternalApiHostLease(
        IApiHostDatabaseLease database,
        ExternalApiHostInfrastructure infrastructure,
        TimeSpan cleanupTimeout)
    {
        _database = database;
        _infrastructure = infrastructure;
        _cleanupTimeout = cleanupTimeout;
    }

    internal Uri BaseAddress { get; private set; } = null!;

    internal ExternalApiHostCleanupReceipt CleanupReceipt { get; private set; } =
        new(true, true, false, [], 0);

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
        var lease = new ExternalApiHostLease(
            request.Database,
            infrastructure,
            TimeSpan.FromSeconds(request.Options.Timeouts.ProcessShutdownSeconds));
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
            lease.CleanupReceipt = lease.CleanupReceipt with { RuntimeDirectoryAbsent = false };
            await lease.StartProcessAsync(request, startupTimeout.Token);
            return lease;
        }
        catch (Exception exception)
        {
            var callerCanceled = exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
            var startupTimedOut = startupTimeout.IsCancellationRequested;
            await lease.WaitForFailedStartupCleanupAsync();

            if (exception is ExternalApiHostCleanupException)
            {
                throw new ExternalApiHostCleanupException(lease.CleanupReceipt);
            }

            if (callerCanceled)
            {
                var cancellationFailure = new OperationCanceledException(
                    CallerCancellationMessage,
                    null,
                    cancellationToken);
                cancellationFailure.Data[nameof(ExternalApiHostCleanupReceipt)] = lease.CleanupReceipt;
                throw cancellationFailure;
            }

            if (startupTimedOut)
            {
                throw new ExternalApiHostStartupException(StartupTimeoutMessage, lease.CleanupReceipt);
            }

            if (exception is ExternalApiHostStartupException startupFailure)
            {
                throw new ExternalApiHostStartupException(startupFailure.Message, lease.CleanupReceipt);
            }

            if (exception is InvalidOperationException invalidOperation &&
                IsPublicationValidationMessage(invalidOperation.Message))
            {
                invalidOperation.Data[nameof(ExternalApiHostCleanupReceipt)] = lease.CleanupReceipt;
                throw;
            }

            throw new ExternalApiHostStartupException(StartupFailureMessage, lease.CleanupReceipt);
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(_cleanupTimeout);
        await DisposeAsync(deadline.Token);
    }

    private async ValueTask DisposeAsync(CancellationToken cleanupToken)
    {
        try
        {
            await _disposeLock.WaitAsync(cleanupToken);
        }
        catch (OperationCanceledException)
        {
            throw new ExternalApiHostCleanupException(CreatePendingCleanupReceipt());
        }

        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            _cleanupAttempt ??= ExternalApiHostCleanup.DisposeAsync(_process, _runtime, _database);
            ExternalApiHostCleanupResult result;
            try
            {
                result = await _cleanupAttempt.WaitAsync(cleanupToken);
            }
            catch (OperationCanceledException) when (cleanupToken.IsCancellationRequested)
            {
                CleanupReceipt = CreatePendingCleanupReceipt();
                throw new ExternalApiHostCleanupException(CleanupReceipt);
            }
            catch (Exception)
            {
                _cleanupAttempt = null;
                CleanupReceipt = CreatePendingCleanupReceipt();
                throw new ExternalApiHostCleanupException(CleanupReceipt);
            }

            _cleanupAttempt = null;
            ApplyCleanupResult(result);
            if (CleanupReceipt.AllResourcesAbsent)
            {
                Volatile.Write(ref _disposed, 1);
            }

            if (result.Receipt.FailureCount != 0 || !CleanupReceipt.AllResourcesAbsent)
            {
                throw new ExternalApiHostCleanupException(CleanupReceipt);
            }
        }
        finally
        {
            _disposeLock.Release();
        }
    }

    private async Task WaitForFailedStartupCleanupAsync()
    {
        _failedStartupCleanup ??= CoordinateFailedStartupCleanupAsync();
        try
        {
            await _failedStartupCleanup.WaitAsync(_cleanupTimeout);
        }
        catch (TimeoutException)
        {
        }
    }

    private async Task<ExternalApiHostCleanupReceipt> CoordinateFailedStartupCleanupAsync()
    {
        while (!CleanupReceipt.AllResourcesAbsent)
        {
            try
            {
                await DisposeAsync(CancellationToken.None);
            }
            catch (ExternalApiHostCleanupException)
            {
            }

            if (!CleanupReceipt.AllResourcesAbsent)
            {
                await Task.Delay(FailedStartupCleanupRetryDelay);
            }
        }

        return CleanupReceipt;
    }

    private ExternalApiHostCleanupReceipt CreatePendingCleanupReceipt() => CleanupReceipt with
    {
        FailureCount = CleanupReceipt.FailureCount + 1
    };

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
            CleanupReceipt = CleanupReceipt with { ProcessTreeAbsent = false };
            var outcome = await _infrastructure.ReadinessMonitor.WaitUntilReadyAsync(
                new Uri(baseAddress, "health/live"),
                _process.Exit,
                new ApiHostReadinessBounds(
                    TimeSpan.FromSeconds(request.Options.Timeouts.HttpRequestSeconds),
                    ReadinessPollInterval),
                startupToken);
            if (outcome == ApiHostReadinessOutcome.Ready)
            {
                if (string.Equals(request.EnvironmentName, TestingEnvironmentName, StringComparison.Ordinal))
                {
                    BaseAddress = baseAddress;
                    return;
                }

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

                throw new ExternalApiHostStartupException(StartupFailureMessage);
            }

            if (outcome == ApiHostReadinessOutcome.AddressInUse && dynamicPort && attempt < maximumAttempts)
            {
                await StopProcessAttemptAsync();
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
        var result = await ExternalApiHostCleanup.DisposeAsync(_process, runtime: null, database: null);
        ApplyCleanupResult(result);
        if (result.Receipt.FailureCount != 0 || !result.Receipt.ProcessTreeAbsent)
        {
            throw new ExternalApiHostCleanupException(CleanupReceipt);
        }
    }

    private void ApplyCleanupResult(ExternalApiHostCleanupResult result)
    {
        if (result.Receipt.ProcessTreeAbsent &&
            result.Receipt.AttemptedCategories.Contains(ExternalApiHostCleanup.ProcessCategory, StringComparer.Ordinal))
        {
            _process = null;
        }

        if (result.Receipt.RuntimeDirectoryAbsent &&
            result.Receipt.AttemptedCategories.Contains(ExternalApiHostCleanup.RuntimeCategory, StringComparer.Ordinal))
        {
            _runtime = null;
        }

        if (result.Receipt.DatabaseAbsent &&
            result.Receipt.AttemptedCategories.Contains(ExternalApiHostCleanup.DatabaseCategory, StringComparer.Ordinal))
        {
            _database = null;
        }

        CleanupReceipt = ExternalApiHostCleanup.Merge(CleanupReceipt, result.Receipt);
        HangfireServerStartObserved |= result.HangfireServerStartObserved;
    }

    private static bool IsPublicationValidationMessage(string message) =>
        message is ApiPublication.RequiredArtifactMessage or ApiPublication.IntegrityMessage or
            ApiPublication.LaunchCommandMessage;
}
