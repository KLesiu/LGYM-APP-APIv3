using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed record ApiHostStartupFailureReceipt(
    string Category,
    bool Ready,
    bool ProcessTreeAbsent,
    bool PrivateRunAbsent,
    bool ContainerAbsent);

internal sealed class RealApiHostProofContext
{
    private RealApiHostProofContext(
        string repositoryRoot,
        E2EOptions options,
        ApiPublication publication)
    {
        RepositoryRoot = repositoryRoot;
        Options = options;
        Publication = publication;
    }

    internal string RepositoryRoot { get; }

    internal E2EOptions Options { get; }

    internal ApiPublication Publication { get; }

    internal static async Task<RealApiHostProofContext> CreateAsync(CancellationToken cancellationToken)
    {
        var repositoryRoot = LgymApi.E2ETests.Harness.RepositoryRoot.Find();
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, repositoryRoot);
        var layout = ApiPublicationLayout.Resolve(repositoryRoot, options.Api.PublishedDllPath);
        layout.EnsureRequiredArtifacts();
        var processRunner = new ExternalProcessRunner();
        var repositoryState = await new ApiRepositoryStateReader(
                processRunner,
                ApiRepositoryStateReader.ResolveGitExecutable())
            .ReadAsync(
                repositoryRoot,
                new ApiRepositoryStateTimeouts(
                    TimeSpan.FromSeconds(Math.Min(options.Timeouts.ApiPublishSeconds, 30)),
                    TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds)),
                cancellationToken);
        var publication = new ApiPublication(
            layout,
            new ApiPublicationReceipt(
                "publish",
                ApiPublication.ComputeDllHash(layout.DllPath),
                DateTimeOffset.UtcNow,
                repositoryState.HeadSha,
                repositoryState.IsDirty,
                new ApiPublicationProcessReceipt(0, false, false)));
        return new RealApiHostProofContext(repositoryRoot, options, publication);
    }

    internal async Task<RealApiHostProofLease> StartAsync(
        string environmentName,
        CancellationToken cancellationToken)
    {
        var database = await PostgreSqlContainerLease.StartAsync(cancellationToken);
        var host = await ExternalApiHostLease.StartAsync(
            new ExternalApiHostRequest(Publication, database, Options, RepositoryRoot)
            {
                EnvironmentName = environmentName
            },
            cancellationToken);
        return new RealApiHostProofLease(host, database, Options);
    }

    internal async Task<ApiHostStartupFailureReceipt> StartWithInvalidCorsAsync(
        CancellationToken cancellationToken)
    {
        var database = await PostgreSqlContainerLease.StartAsync(cancellationToken);
        var runtimeFactory = new Task7RuntimeFactory();
        var processStarter = new Task7ProcessStarter();
        try
        {
            var host = await ExternalApiHostLease.StartAsync(
                new ExternalApiHostCompositionRequest(
                    Publication,
                    new PostgreSqlApiHostDatabaseLease(database),
                    Options,
                    RepositoryRoot)
                {
                    EnvironmentName = "E2E",
                    CorsAllowedOrigins = ["http://localhost:8083", "http://localhost:8084"]
                },
                new ExternalApiHostInfrastructure(
                    runtimeFactory,
                    processStarter,
                    new ApiHostReadinessMonitor(),
                    new LoopbackPortAllocator()),
                cancellationToken);
            await host.DisposeAsync();
            throw new AssertionException("The broadened E2E CORS host unexpectedly reached readiness.");
        }
        catch (ExternalApiHostStartupException exception)
        {
            return new ApiHostStartupFailureReceipt(
                exception.Message,
                Ready: false,
                ProcessTreeAbsent: processStarter.ExactProcessTreeAbsent,
                PrivateRunAbsent: runtimeFactory.RunDirectory is null || !Directory.Exists(runtimeFactory.RunDirectory),
                ContainerAbsent: await database.ConfirmAbsentAsync());
        }
    }
}

internal sealed class RealApiHostProofLease : IAsyncDisposable
{
    private readonly ExternalApiHostLease _host;
    private readonly PostgreSqlContainerLease _database;
    private int _disposed;

    internal RealApiHostProofLease(
        ExternalApiHostLease host,
        PostgreSqlContainerLease database,
        E2EOptions options)
    {
        _host = host;
        _database = database;
        Client = new HttpClient
        {
            BaseAddress = host.BaseAddress,
            Timeout = TimeSpan.FromSeconds(options.Timeouts.HttpRequestSeconds)
        };
    }

    internal HttpClient Client { get; }

    internal ExternalApiHostCleanupReceipt CleanupReceipt => _host.CleanupReceipt;

    internal bool HangfireServerStartObserved => _host.HangfireServerStartObserved;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Client.Dispose();
        await _host.DisposeAsync();
        if (!await _database.ConfirmAbsentAsync())
        {
            throw new InvalidOperationException("Real API host PostgreSQL cleanup could not be proven.");
        }
    }
}
