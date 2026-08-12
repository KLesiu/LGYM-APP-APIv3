using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed class ExternalApiHostTestFixture : IDisposable
{
    private readonly string _fixtureRoot;

    internal ExternalApiHostTestFixture()
    {
        RepositoryRoot = LgymApi.E2ETests.Harness.RepositoryRoot.Find();
        _fixtureRoot = Path.Combine(
            RepositoryRoot,
            ".e2e-private",
            "task5-fixtures",
            Guid.NewGuid().ToString("N"));
        var publicationDirectory = Path.Combine(_fixtureRoot, "publication");
        Directory.CreateDirectory(publicationDirectory);
        var configuredDllPath = Path.GetRelativePath(
            RepositoryRoot,
            Path.Combine(publicationDirectory, ApiPublicationLayout.DllFileName));
        var layout = ApiPublicationLayout.Resolve(RepositoryRoot, configuredDllPath);
        File.WriteAllText(layout.DllPath, "task-5-synthetic-dll");
        File.WriteAllText(layout.DependenciesPath, "{}");
        File.WriteAllText(layout.RuntimeConfigurationPath, "{}");
        Publication = new ApiPublication(
            layout,
            new ApiPublicationReceipt(
                "publish",
                ApiPublication.ComputeDllHash(layout.DllPath),
                DateTimeOffset.UtcNow,
                new string('a', 40),
                false,
                new ApiPublicationProcessReceipt(0, false, false)));
        Options = CreateOptions();
    }

    internal string RepositoryRoot { get; }

    internal ApiPublication Publication { get; }

    internal E2EOptions Options { get; }

    internal ExternalApiHostCompositionRequest CreateRequest(FakeApiHostDatabaseLease database) =>
        new(Publication, database, Options, RepositoryRoot);

    internal ExternalApiHostInfrastructure CreateInfrastructure(
        FakeApiHostRuntimeFactory runtimeFactory,
        FakeExternalApiProcessStarter processStarter,
        IApiHostReadinessMonitor readinessMonitor,
        FakeLoopbackPortAllocator portAllocator) =>
        new(runtimeFactory, processStarter, readinessMonitor, portAllocator);

    public void Dispose()
    {
        var task5Root = Path.GetDirectoryName(_fixtureRoot)!;
        if (Directory.Exists(_fixtureRoot))
        {
            Directory.Delete(_fixtureRoot, recursive: true);
        }

        if (Directory.Exists(task5Root) && !Directory.EnumerateFileSystemEntries(task5Root).Any())
        {
            Directory.Delete(task5Root);
        }
    }

    private static E2EOptions CreateOptions() => new()
    {
        Api = new E2EApiOptions
        {
            PublishedDllPath = ".e2e-private/published-api/LgymApi.Api.dll",
            Port = 0
        },
        Runtime = new E2ERuntimeOptions
        {
            PrivateRunRoot = ".e2e-private/runs"
        },
        Timeouts = new E2ETimeoutsOptions
        {
            ApiStartupSeconds = 120,
            ProcessShutdownSeconds = 15,
            HttpRequestSeconds = 30,
            TestSessionSeconds = 900
        }
    };
}
