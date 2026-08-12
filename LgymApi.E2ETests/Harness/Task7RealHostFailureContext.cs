using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed record Task7StartupFailureReceipt(
    string Category,
    bool Ready,
    bool ProcessTreeAbsent,
    bool PrivateRunAbsent,
    bool ConfigurationAbsent,
    bool ContainerAbsent);

internal sealed class Task7RealHostFailureContext(RealApiHostProofContext context)
{
    private const string UnreachableConnection =
        "Host=192.0.2.1;Port=5432;Database=unreachable;Username=unreachable;Password=unreachable;Timeout=30";

    internal Task<Task7StartupFailureReceipt> StartWithPendingMigrationsAsync(
        string environmentName,
        CancellationToken cancellationToken) =>
        StartAsync(environmentName, ApiRuntimeConfigurationProfile.SyntheticCloudflareR2, null, cancellationToken);

    internal Task<Task7StartupFailureReceipt> StartWithUnreachableDatabaseAsync(CancellationToken cancellationToken) =>
        StartAsync("E2E", ApiRuntimeConfigurationProfile.E2E, UnreachableConnection, cancellationToken);

    private async Task<Task7StartupFailureReceipt> StartAsync(
        string environmentName,
        ApiRuntimeConfigurationProfile profile,
        string? connectionString,
        CancellationToken cancellationToken)
    {
        var database = await PostgreSqlContainerLease.StartAsync(cancellationToken);
        var trackedDatabase = new Task7DatabaseLease(database, connectionString);
        var runtimeFactory = new Task7RuntimeFactory();
        var processStarter = new Task7ProcessStarter();
        var hostOptions = CreateHostOptions(connectionString is null ? context.Options.Timeouts.ApiStartupSeconds : 3);

        try
        {
            var host = await ExternalApiHostLease.StartAsync(
                new ExternalApiHostCompositionRequest(context.Publication, trackedDatabase, hostOptions, context.RepositoryRoot)
                {
                    EnvironmentName = environmentName,
                    RuntimeProfile = profile
                },
                new ExternalApiHostInfrastructure(
                    runtimeFactory,
                    processStarter,
                    new ApiHostReadinessMonitor(),
                    new LoopbackPortAllocator()),
                cancellationToken);
            await host.DisposeAsync();
            throw new AssertionException("The adverse API host unexpectedly reached readiness.");
        }
        catch (ExternalApiHostStartupException exception)
        {
            return new Task7StartupFailureReceipt(
                exception.Message,
                Ready: false,
                ProcessTreeAbsent: processStarter.ExactProcessTreeAbsent,
                PrivateRunAbsent: runtimeFactory.RunDirectory is null || !Directory.Exists(runtimeFactory.RunDirectory),
                ConfigurationAbsent: runtimeFactory.ConfigurationPath is null || !File.Exists(runtimeFactory.ConfigurationPath),
                ContainerAbsent: await database.ConfirmAbsentAsync());
        }
    }

    private E2EOptions CreateHostOptions(int apiStartupSeconds) => new()
    {
        Api = new E2EApiOptions
        {
            PublishedDllPath = context.Options.Api.PublishedDllPath,
            Port = context.Options.Api.Port
        },
        Runtime = new E2ERuntimeOptions { PrivateRunRoot = context.Options.Runtime.PrivateRunRoot },
        Timeouts = new E2ETimeoutsOptions
        {
            ApiStartupSeconds = apiStartupSeconds,
            ProcessShutdownSeconds = context.Options.Timeouts.ProcessShutdownSeconds,
            HttpRequestSeconds = context.Options.Timeouts.HttpRequestSeconds,
            TestSessionSeconds = context.Options.Timeouts.TestSessionSeconds
        }
    };
}

internal sealed class Task7DatabaseLease(PostgreSqlContainerLease database, string? connectionString) : IApiHostDatabaseLease
{
    public string ConnectionString => connectionString ?? database.ConnectionString;

    public ValueTask DisposeAsync() => database.DisposeAsync();
}

internal sealed class Task7RuntimeFactory : IApiHostRuntimeLeaseFactory
{
    private readonly ApiHostRuntimeLeaseFactory _inner = new();

    internal string? ConfigurationPath { get; private set; }

    internal string? RunDirectory { get; private set; }

    public async Task<IApiHostRuntimeLease> CreateAsync(RuntimeConfigurationRequest request, CancellationToken cancellationToken)
    {
        var lease = await _inner.CreateAsync(request, cancellationToken);
        ConfigurationPath = lease.ConfigurationPath;
        RunDirectory = Path.GetDirectoryName(Path.GetDirectoryName(lease.ConfigurationPath)!);
        return lease;
    }
}

internal sealed class Task7ProcessStarter : IExternalApiProcessStarter
{
    private readonly ExternalApiProcessStarter _inner = new();
    private ExternalApiProcessLease? _process;

    internal bool ExactProcessTreeAbsent => _process?.ExactProcessTreeAbsent ?? true;

    public IExternalApiProcess Start(ExternalProcessRequest request)
    {
        _process = _inner.Start(request) as ExternalApiProcessLease;
        return _process ?? throw new InvalidOperationException("Task 7 process observation is unavailable.");
    }
}
