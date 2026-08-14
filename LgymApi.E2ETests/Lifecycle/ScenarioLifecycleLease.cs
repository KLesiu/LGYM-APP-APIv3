using System.Runtime.ExceptionServices;
using System.Text.Json;
using LgymApi.E2ETests.Browser;
using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Harness;
using Microsoft.Playwright;

namespace LgymApi.E2ETests.Lifecycle;

internal sealed record ScenarioLifecycleRequest(
    LifecycleRunDirectoryLease Run,
    WebSourceRunLease? Source,
    E2EOptions Options,
    ApiPublication Publication,
    string RepositoryRoot,
    string CaseId,
    ScenarioLifecycleObservation? Previous = null);

internal sealed record ScenarioLifecycleReceipt(
    IReadOnlyList<string> AcquiredCategories,
    IReadOnlyList<string> AttemptedCleanupCategories,
    int CleanupFailureCount,
    bool DatabaseIdentityDistinct,
    bool PreviousResourcesAbsent,
    bool BrowserStorageEmpty,
    bool DatabaseAbsent,
    bool ApiAbsent,
    bool ExpoAbsent,
    bool ScenarioPathsAbsent)
{
    public override string ToString() => "<scenario-lifecycle-receipt>";
}

internal sealed class ScenarioLifecycleObservation
{
    private readonly ScenarioResourceObservation _database;
    private readonly ExternalApiHostObservation _api;
    private readonly ExpoWebIdentity _expo;
    private readonly Func<ScenarioLifecycleReceipt> _receipt;

    internal ScenarioLifecycleObservation(
        ScenarioResourceObservation database,
        ExternalApiHostObservation api,
        ExpoWebIdentity expo,
        Func<ScenarioLifecycleReceipt> receipt)
    {
        _database = database;
        _api = api;
        _expo = expo;
        _receipt = receipt;
    }

    internal bool IsDistinctFrom(ScenarioLifecycleObservation previous) =>
        !_database.Identity.Equals(previous._database.Identity) &&
        !_api.Identity.Equals(previous._api.Identity) &&
        !_expo.Equals(previous._expo);

    internal async Task<bool> ConfirmAbsentAsync()
    {
        var facts = _receipt();
        return facts.DatabaseAbsent && facts.ApiAbsent && facts.ExpoAbsent && facts.ScenarioPathsAbsent &&
               await _database.ConfirmAbsentAsync() && await _api.ConfirmAbsentAsync();
    }

    public override string ToString() => "<scenario-lifecycle-observation>";
}

internal sealed class ScenarioLifecycleCleanupException(ScenarioLifecycleReceipt receipt)
    : InvalidOperationException("E2E scenario lifecycle cleanup failed.")
{
    internal ScenarioLifecycleReceipt Receipt { get; } = receipt;
}

internal interface IScenarioLifecycleApiHost : IAsyncDisposable
{
    Uri BaseAddress { get; }

    ExternalApiHostObservation Observation { get; }
}

internal interface IScenarioLifecycleExpo : IAsyncDisposable
{
    ExpoWebIdentity Identity { get; }

    Task<bool> ConfirmAbsentAsync();
}

internal interface IScenarioLifecycleBrowserRun : IAsyncDisposable;

internal interface IScenarioLifecycleBrowserScenario : IAsyncDisposable
{
    IPage Page { get; }

    Task<bool> ConfirmStorageIsEmptyAsync();
}

internal interface IScenarioLifecycleDependencies
{
    Task<ScenarioDatabaseOwnership> StartDatabaseAsync(CancellationToken cancellationToken);

    Task<IScenarioLifecycleApiHost> StartApiHostAsync(
        IApiHostDatabaseLease database,
        LifecycleComponentDirectoryLease apiRuntime,
        ScenarioLifecycleRequest request,
        CancellationToken cancellationToken);

    Task<IScenarioLifecycleExpo> StartExpoAsync(
        LifecycleComponentDirectoryLease webRuntime,
        Uri apiBaseAddress,
        ScenarioLifecycleRequest request,
        CancellationToken cancellationToken);

    Task<IScenarioLifecycleBrowserRun> StartBrowserRunAsync(
        LifecycleComponentDirectoryLease browserRuntime,
        ScenarioLifecycleRequest request,
        CancellationToken cancellationToken);

    Task<IScenarioLifecycleBrowserScenario> StartBrowserScenarioAsync(
        IScenarioLifecycleBrowserRun browserRun,
        ScenarioLifecycleRequest request,
        CancellationToken cancellationToken);
}

internal sealed class ScenarioLifecycleLease : IAsyncDisposable
{
    private const string ScenarioPathsCategory = "scenario-paths";
    private const string PostgreSqlCategory = "postgresql";
    private const string ApiCategory = "external-api-host";
    private const string ExpoCategory = "expo";
    private const string BrowserRunCategory = "browser-run";
    private const string BrowserScenarioCategory = "browser-scenario";

    private readonly TimeSpan _shutdownTimeout;
    private readonly List<string> _acquiredCategories = [];
    private readonly object _cleanupLock = new();
    private Task<ScenarioLifecycleReceipt>? _cleanup;
    private LifecycleScenarioDirectoryLease? _scenarioPaths;
    private ScenarioDatabaseOwnership? _scenarioDatabase;
    private ScenarioResourceObservation? _databaseObservation;
    private IScenarioLifecycleApiHost? _api;
    private IScenarioLifecycleExpo? _expo;
    private IScenarioLifecycleBrowserRun? _browserRun;
    private IScenarioLifecycleBrowserScenario? _browserScenario;
    private ScenarioLifecycleObservation? _observation;

    private ScenarioLifecycleLease(TimeSpan shutdownTimeout)
    {
        _shutdownTimeout = shutdownTimeout;
        Receipt = EmptyReceipt();
    }

    internal IPage Page => _browserScenario?.Page
        ?? throw new InvalidOperationException("E2E scenario browser page is unavailable.");

    internal ScenarioLifecycleObservation Observation => _observation
        ?? throw new InvalidOperationException("E2E scenario lifecycle observation is unavailable.");

    internal ScenarioLifecycleReceipt Receipt { get; private set; }

    internal static async Task<ScenarioLifecycleLease> CreateAsync(
        ScenarioLifecycleRequest request,
        IScenarioLifecycleDependencies? dependencies = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var shutdownTimeout = TimeSpan.FromSeconds(request.Options.Timeouts.ProcessShutdownSeconds);
        var scenarioTimeout = TimeSpan.FromSeconds(request.Options.Timeouts.ScenarioSeconds);
        if (shutdownTimeout <= TimeSpan.Zero || scenarioTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("E2E scenario lifecycle configuration is invalid.");
        }

        dependencies ??= DefaultScenarioLifecycleDependencies.Instance;
        var lease = new ScenarioLifecycleLease(shutdownTimeout);
        using var scenarioTimeoutSource = new CancellationTokenSource(scenarioTimeout);
        using var scenarioLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            scenarioTimeoutSource.Token);

        try
        {
            var previousAbsent = request.Previous is null || await request.Previous.ConfirmAbsentAsync();
            if (!previousAbsent)
            {
                throw new InvalidOperationException("A prior E2E scenario resource is still present.");
            }

            lease._scenarioPaths = request.Run.CreateScenario(request.CaseId);
            lease._acquiredCategories.Add(ScenarioPathsCategory);

            lease._scenarioDatabase = await dependencies.StartDatabaseAsync(scenarioLifetime.Token);
            lease._databaseObservation = lease._scenarioDatabase.Observation;
            lease._acquiredCategories.Add(PostgreSqlCategory);

            var apiRuntime = lease._scenarioPaths.CreateApiComponent();
            var apiDatabase = lease._scenarioDatabase.TransferToApiHost();
            lease._scenarioDatabase = null;
            lease._api = await dependencies.StartApiHostAsync(
                apiDatabase,
                apiRuntime,
                request,
                scenarioLifetime.Token);
            lease._acquiredCategories.Add(ApiCategory);

            var webRuntime = lease._scenarioPaths.CreateWebRuntimeComponent();
            lease._expo = await dependencies.StartExpoAsync(
                webRuntime,
                lease._api.BaseAddress,
                request,
                scenarioLifetime.Token);
            lease._acquiredCategories.Add(ExpoCategory);

            var browserRuntime = lease._scenarioPaths.CreateBrowserRuntimeComponent();
            lease._browserRun = await dependencies.StartBrowserRunAsync(
                browserRuntime,
                request,
                scenarioLifetime.Token);
            lease._acquiredCategories.Add(BrowserRunCategory);

            lease._browserScenario = await dependencies.StartBrowserScenarioAsync(
                lease._browserRun,
                request,
                scenarioLifetime.Token);
            lease._acquiredCategories.Add(BrowserScenarioCategory);

            var storageIsEmpty = await lease._browserScenario.ConfirmStorageIsEmptyAsync();
            lease._observation = new ScenarioLifecycleObservation(
                lease._databaseObservation,
                lease._api.Observation,
                lease._expo.Identity,
                () => lease.Receipt);
            lease.Receipt = lease.Receipt with
            {
                AcquiredCategories = lease._acquiredCategories.ToArray(),
                DatabaseIdentityDistinct = request.Previous is null || lease._observation.IsDistinctFrom(request.Previous),
                PreviousResourcesAbsent = previousAbsent,
                BrowserStorageEmpty = storageIsEmpty
            };
            return lease;
        }
        catch (Exception exception)
        {
            await lease.CleanupAsync();
            exception.Data[nameof(ScenarioLifecycleReceipt)] = lease.Receipt;
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var receipt = await GetCleanupTask();
        if (receipt.CleanupFailureCount != 0)
        {
            throw new ScenarioLifecycleCleanupException(receipt);
        }
    }

    public override string ToString() => "<scenario-lifecycle-lease>";

    private Task<ScenarioLifecycleReceipt> GetCleanupTask()
    {
        lock (_cleanupLock)
        {
            return _cleanup ??= CleanupAsync();
        }
    }

    private async Task<ScenarioLifecycleReceipt> CleanupAsync()
    {
        var categories = new List<string>();
        var failures = 0;
        failures += await DisposeOwnedAsync(BrowserScenarioCategory, _browserScenario, categories);
        _browserScenario = null;
        failures += await DisposeOwnedAsync(BrowserRunCategory, _browserRun, categories);
        _browserRun = null;
        failures += await DisposeOwnedAsync(ExpoCategory, _expo, categories);
        var expoAbsent = _expo is null || await ConfirmAbsentAsync(_expo);
        _expo = null;
        failures += await DisposeOwnedAsync(ApiCategory, _api, categories);
        var apiAbsent = _api is null || await ConfirmAbsentAsync(_api.Observation);
        _api = null;
        failures += await DisposeOwnedAsync(PostgreSqlCategory, _scenarioDatabase, categories);
        _scenarioDatabase = null;
        failures += await DisposeOwnedAsync(ScenarioPathsCategory, _scenarioPaths, categories);
        var pathsAbsent = _scenarioPaths is null || !Directory.Exists(_scenarioPaths.ScenarioDirectory);
        _scenarioPaths = null;
        var databaseAbsent = await ConfirmAbsentAsync(_databaseObservation);

        Receipt = Receipt with
        {
            AcquiredCategories = _acquiredCategories.ToArray(),
            AttemptedCleanupCategories = categories,
            CleanupFailureCount = failures,
            DatabaseAbsent = databaseAbsent,
            ApiAbsent = apiAbsent,
            ExpoAbsent = expoAbsent,
            ScenarioPathsAbsent = pathsAbsent
        };
        return Receipt;
    }

    private async Task<int> DisposeOwnedAsync(string category, IAsyncDisposable? resource, ICollection<string> categories)
    {
        if (resource is null)
        {
            return 0;
        }

        categories.Add(category);
        try
        {
            using var shutdown = new CancellationTokenSource(_shutdownTimeout);
            await resource.DisposeAsync().AsTask().WaitAsync(shutdown.Token);
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private static async Task<bool> ConfirmAbsentAsync(ScenarioResourceObservation? database)
    {
        try
        {
            return database is not null && await database.ConfirmAbsentAsync();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<bool> ConfirmAbsentAsync(IScenarioLifecycleExpo expo)
    {
        try
        {
            return await expo.ConfirmAbsentAsync();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<bool> ConfirmAbsentAsync(ExternalApiHostObservation api)
    {
        try
        {
            return await api.ConfirmAbsentAsync();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ScenarioLifecycleReceipt EmptyReceipt() => new([], [], 0, false, false, false, false, false, false, false);
}

internal sealed class DefaultScenarioLifecycleDependencies : IScenarioLifecycleDependencies
{
    internal static DefaultScenarioLifecycleDependencies Instance { get; } = new();

    public async Task<ScenarioDatabaseOwnership> StartDatabaseAsync(CancellationToken cancellationToken)
    {
        var database = await PostgreSqlContainerLease.StartAsync(cancellationToken);
        return new ScenarioDatabaseOwnership(
            new PostgreSqlApiHostDatabaseLease(database),
            database.CreateObservation());
    }

    public async Task<IScenarioLifecycleApiHost> StartApiHostAsync(
        IApiHostDatabaseLease database,
        LifecycleComponentDirectoryLease apiRuntime,
        ScenarioLifecycleRequest request,
        CancellationToken cancellationToken) => new ExternalApiHostScenarioResource(await ExternalApiHostLease.StartAsync(
            new ExternalApiHostCompositionRequest(
                request.Publication,
                database,
                request.Options,
                request.RepositoryRoot)
            {
                ApiRuntimeDirectory = apiRuntime
            },
            cancellationToken));

    public async Task<IScenarioLifecycleExpo> StartExpoAsync(
        LifecycleComponentDirectoryLease webRuntime,
        Uri apiBaseAddress,
        ScenarioLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        var source = request.Source ?? throw new InvalidOperationException("E2E scenario web source is unavailable.");
        return new ExpoScenarioResource(await ExpoWebLease.StartAsync(
            new ExpoWebStartRequest(source, apiBaseAddress)
            {
                Options = request.Options,
                RuntimeDirectory = webRuntime
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IScenarioLifecycleBrowserRun> StartBrowserRunAsync(
        LifecycleComponentDirectoryLease browserRuntime,
        ScenarioLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new BrowserRunScenarioResource(await BrowserRunLease.CreateAsync(new BrowserRunRequest(
            request.Run.RunLease,
            request.Options.Timeouts.BrowserActionMilliseconds)));
    }

    public async Task<IScenarioLifecycleBrowserScenario> StartBrowserScenarioAsync(
        IScenarioLifecycleBrowserRun browserRun,
        ScenarioLifecycleRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (browserRun is not BrowserRunScenarioResource browser)
        {
            throw new InvalidOperationException("E2E scenario browser run is invalid.");
        }

        return new BrowserScenarioResource(await BrowserScenarioLease.CreateAsync(
            browser.Lease,
            request.Options.Timeouts.BrowserActionMilliseconds));
    }

    private sealed class ExternalApiHostScenarioResource(ExternalApiHostLease lease) : IScenarioLifecycleApiHost
    {
        public Uri BaseAddress => lease.BaseAddress;

        public ExternalApiHostObservation Observation { get; } = lease.CreateObservation();

        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }

    private sealed class ExpoScenarioResource(ExpoWebLease lease) : IScenarioLifecycleExpo
    {
        public ExpoWebIdentity Identity => lease.Identity;

        public ValueTask DisposeAsync() => lease.DisposeAsync();

        public Task<bool> ConfirmAbsentAsync() => Task.FromResult(
            lease.CleanupReceipt is { ProcessTreeAbsent: true, DrainsCompleted: true, InspectionCompleted: true });
    }

    private sealed class BrowserRunScenarioResource(BrowserRunLease lease) : IScenarioLifecycleBrowserRun
    {
        internal BrowserRunLease Lease { get; } = lease;

        public ValueTask DisposeAsync() => Lease.DisposeAsync();
    }

    private sealed class BrowserScenarioResource(BrowserScenarioLease lease) : IScenarioLifecycleBrowserScenario
    {
        public IPage Page => lease.Page;

        public ValueTask DisposeAsync() => lease.DisposeAsync();

        public async Task<bool> ConfirmStorageIsEmptyAsync()
        {
            var storageState = await lease.Context.StorageStateAsync();
            using var document = JsonDocument.Parse(storageState);
            return document.RootElement.GetProperty("cookies").GetArrayLength() == 0 &&
                   document.RootElement.GetProperty("origins").GetArrayLength() == 0;
        }
    }
}
