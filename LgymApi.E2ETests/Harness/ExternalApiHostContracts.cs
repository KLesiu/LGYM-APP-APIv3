using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Lifecycle;

namespace LgymApi.E2ETests.Harness;

internal sealed record ExternalApiHostRequest(
    ApiPublication Publication,
    PostgreSqlContainerLease PostgreSql,
    E2EOptions Options,
    string RepositoryRoot)
{
    internal string EnvironmentName { get; init; } = "E2E";

    internal IReadOnlyList<string>? CorsAllowedOrigins { get; init; }
}

internal sealed record ExternalApiHostCompositionRequest(
    ApiPublication Publication,
    IApiHostDatabaseLease Database,
    E2EOptions Options,
    string RepositoryRoot)
{
    internal string EnvironmentName { get; init; } = "E2E";

    internal IReadOnlyList<string>? CorsAllowedOrigins { get; init; }

    internal ApiRuntimeConfigurationProfile RuntimeProfile { get; init; } = ApiRuntimeConfigurationProfile.E2E;

    internal LifecycleComponentDirectoryLease? ApiRuntimeDirectory { get; init; }
}

internal interface IApiHostDatabaseLease : IAsyncDisposable
{
    string ConnectionString { get; }
}

internal interface IApiHostDatabaseAbsenceObservation
{
    Task<bool> ConfirmAbsentAsync();
}

internal interface IApiHostRuntimeLease : IAsyncDisposable
{
    string ConfigurationPath { get; }

    string PrivateTempDirectory { get; }

    bool RuntimeDirectoryAbsent { get; }
}

internal interface IApiHostRuntimeLeaseFactory
{
    Task<IApiHostRuntimeLease> CreateAsync(
        RuntimeConfigurationRequest request,
        CancellationToken cancellationToken);
}

internal sealed record ExternalApiHostInfrastructure(
    IApiHostRuntimeLeaseFactory RuntimeFactory,
    IExternalApiProcessStarter ProcessStarter,
    IApiHostReadinessMonitor ReadinessMonitor,
    ILoopbackPortAllocator PortAllocator,
    IDatabaseBackedApiReadinessProbe? DatabaseReadinessProbe = null)
{
    internal static ExternalApiHostInfrastructure CreateDefault() => new(
        new ApiHostRuntimeLeaseFactory(),
        new ExternalApiProcessStarter(),
        new ApiHostReadinessMonitor(),
        new LoopbackPortAllocator(),
        new DatabaseBackedApiReadinessProbe());
}

internal enum DatabaseBackedApiReadinessOutcome
{
    Ready,
    HttpFailure,
    HttpTimeout,
    UnexpectedStatus
}

internal interface IDatabaseBackedApiReadinessProbe
{
    Task<DatabaseBackedApiReadinessOutcome> WaitUntilReadyAsync(
        Uri baseAddress,
        ApiHostReadinessBounds bounds,
        CancellationToken cancellationToken);
}

internal sealed record ExternalApiHostCleanupReceipt(
    bool ProcessTreeAbsent,
    bool RuntimeDirectoryAbsent,
    bool DatabaseAbsent,
    IReadOnlyList<string> AttemptedCategories,
    int FailureCount,
    bool AttemptHistoryTruncated = false,
    bool FailureCountSaturated = false)
{
    internal bool AllResourcesAbsent => ProcessTreeAbsent && RuntimeDirectoryAbsent && DatabaseAbsent;

    public override string ToString() => "<external-api-host-cleanup>";
}

internal sealed class ExternalApiHostObservation(
    ScenarioResourceIdentity identity,
    Func<ExternalApiHostCleanupReceipt> cleanupReceipt)
{
    internal ScenarioResourceIdentity Identity { get; } = identity;

    internal ExternalApiHostCleanupReceipt CleanupReceipt => cleanupReceipt();

    internal Task<bool> ConfirmAbsentAsync() => Task.FromResult(
        CleanupReceipt.ProcessTreeAbsent &&
        CleanupReceipt.RuntimeDirectoryAbsent &&
        CleanupReceipt.DatabaseAbsent);

    public override string ToString() => "<external-api-host-observation>";
}

internal sealed class ExternalApiHostStartupException : InvalidOperationException
{
    internal ExternalApiHostStartupException(
        string message,
        ExternalApiHostCleanupReceipt? cleanupReceipt = null,
        Task<ExternalApiHostCleanupReceipt>? cleanupCompletion = null) : base(message)
    {
        CleanupReceipt = cleanupReceipt ?? new ExternalApiHostCleanupReceipt(false, false, false, [], 0);
        CleanupCompletion = cleanupCompletion ?? Task.FromResult(CleanupReceipt);
    }

    internal Task<ExternalApiHostCleanupReceipt> CleanupCompletion { get; }

    internal ExternalApiHostCleanupReceipt CleanupReceipt { get; }
}

internal sealed class ExternalApiHostCleanupException(ExternalApiHostCleanupReceipt receipt)
    : InvalidOperationException(ExternalApiHostLease.CleanupFailureMessage)
{
    internal ExternalApiHostCleanupReceipt Receipt { get; } = receipt;
}

internal sealed class PostgreSqlApiHostDatabaseLease(PostgreSqlContainerLease lease)
    : IApiHostDatabaseLease, IApiHostDatabaseAbsenceObservation
{
    public string ConnectionString => lease.ConnectionString;

    public ValueTask DisposeAsync() => lease.DisposeAsync();

    public Task<bool> ConfirmAbsentAsync() => lease.ConfirmAbsentAsync();

    public override string ToString() => "<api-host-database-lease>";
}

internal sealed class ApiHostRuntimeLeaseFactory : IApiHostRuntimeLeaseFactory
{
    public async Task<IApiHostRuntimeLease> CreateAsync(
        RuntimeConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var lease = request.ApiRuntimeDirectory is null
            ? await RuntimeConfigurationLease.CreateAsync(request, cancellationToken)
            : await RuntimeConfigurationLease.CreateAsync(request, request.ApiRuntimeDirectory, cancellationToken);
        try
        {
            return new ApiHostRuntimeLease(lease);
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }
}

internal sealed class ApiHostRuntimeLease : IApiHostRuntimeLease
{
    private readonly RuntimeConfigurationLease _lease;

    internal ApiHostRuntimeLease(RuntimeConfigurationLease lease)
    {
        _lease = lease;
        PrivateTempDirectory = lease.CreatePrivateTempDirectory();
    }

    public string ConfigurationPath => _lease.ConfigurationPath;

    public string PrivateTempDirectory { get; }

    public bool RuntimeDirectoryAbsent { get; private set; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _lease.DisposeAsync();
        }
        finally
        {
            RuntimeDirectoryAbsent = !Directory.Exists(_lease.RunDirectory);
        }
    }

    public override string ToString() => "<api-host-runtime-lease>";
}
