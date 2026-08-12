using LgymApi.E2ETests.Configuration;

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
}

internal interface IApiHostDatabaseLease : IAsyncDisposable
{
    string ConnectionString { get; }
}

internal interface IApiHostRuntimeLease : IAsyncDisposable
{
    string ConfigurationPath { get; }

    string PrivateTempDirectory { get; }
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
    ILoopbackPortAllocator PortAllocator)
{
    internal static ExternalApiHostInfrastructure CreateDefault() => new(
        new ApiHostRuntimeLeaseFactory(),
        new ExternalApiProcessStarter(),
        new ApiHostReadinessMonitor(),
        new LoopbackPortAllocator());
}

internal sealed record ExternalApiHostCleanupReceipt(
    IReadOnlyList<string> AttemptedCategories,
    int FailureCount)
{
    public override string ToString() => "<external-api-host-cleanup>";
}

internal sealed class ExternalApiHostStartupException(string message) : InvalidOperationException(message);

internal sealed class ExternalApiHostCleanupException(ExternalApiHostCleanupReceipt receipt)
    : InvalidOperationException(ExternalApiHostLease.CleanupFailureMessage)
{
    internal ExternalApiHostCleanupReceipt Receipt { get; } = receipt;
}

internal sealed class PostgreSqlApiHostDatabaseLease(PostgreSqlContainerLease lease) : IApiHostDatabaseLease
{
    public string ConnectionString => lease.ConnectionString;

    public ValueTask DisposeAsync() => lease.DisposeAsync();

    public override string ToString() => "<api-host-database-lease>";
}

internal sealed class ApiHostRuntimeLeaseFactory : IApiHostRuntimeLeaseFactory
{
    public async Task<IApiHostRuntimeLease> CreateAsync(
        RuntimeConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        var lease = await RuntimeConfigurationLease.CreateAsync(request, cancellationToken);
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

    public ValueTask DisposeAsync() => _lease.DisposeAsync();

    public override string ToString() => "<api-host-runtime-lease>";
}
