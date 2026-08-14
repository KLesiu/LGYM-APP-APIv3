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
    private readonly SemaphoreSlim _cleanupGate = new(1, 1);
    private readonly List<string> _cleanupCategories = [];
    private Task<ScenarioLifecycleReceipt>? _cleanup;
    private Task? _retainedCleanupObservation;
    private IScenarioLifecycleAcquisition? _browserAcquisition;
    private ScenarioLifecycleCleanupStage _cleanupStage;
    private int _cleanupFailures;
    private bool _databaseAbsent;
    private bool _apiAbsent;
    private bool _expoAbsent;
    private bool _scenarioPathsAbsent;
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

    internal static async Task<ScenarioLifecycleReceipt> ExecuteAsync(
        ScenarioLifecycleRequest request,
        Func<ScenarioLifecycleLease, CancellationToken, Task> callback,
        ScenarioFailureArtifactWriter artifactWriter,
        IScenarioLifecycleDependencies? dependencies = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(artifactWriter);

        var lease = await CreateAsync(request, dependencies, cancellationToken);
        var artifactOwner = lease.GetFailureArtifactOwner();
        try
        {
            await callback(lease, cancellationToken);
            await lease.DisposeAsync();
            return lease.Receipt;
        }
        catch (Exception exception)
        {
            await lease.FinalizeFailureAsync(artifactOwner, artifactWriter);
            exception.Data[nameof(ScenarioLifecycleReceipt)] = lease.Receipt;
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
    }

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
            var browserRun = await lease.AcquireBrowserResourceAsync(
                ScenarioLifecycleAcquisitionStage.BrowserRun,
                () => dependencies.StartBrowserRunAsync(browserRuntime, request, scenarioLifetime.Token),
                resource => lease._browserRun = resource,
                BrowserRunCategory,
                scenarioLifetime.Token);

            var browserScenario = await lease.AcquireBrowserResourceAsync(
                ScenarioLifecycleAcquisitionStage.BrowserScenario,
                () => dependencies.StartBrowserScenarioAsync(browserRun, request, scenarioLifetime.Token),
                resource => lease._browserScenario = resource,
                BrowserScenarioCategory,
                scenarioLifetime.Token);

            var storageIsEmpty = await browserScenario.ConfirmStorageIsEmptyAsync();
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
            await lease.GetCleanupTask();
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

    internal LifecycleScenarioDirectoryLease GetFailureArtifactOwner() => _scenarioPaths
        ?? throw new InvalidOperationException("E2E scenario failure artifact ownership is unavailable.");

    internal async Task FinalizeFailureAsync(
        LifecycleScenarioDirectoryLease artifactOwner,
        ScenarioFailureArtifactWriter artifactWriter)
    {
        await GetCleanupTask();
        using var diagnosticsTimeout = new CancellationTokenSource(_shutdownTimeout);
        try
        {
            await artifactWriter.WriteAsync(artifactOwner, Receipt, diagnosticsTimeout.Token);
        }
        catch (Exception)
        {
            _cleanupFailures++;
            UpdateReceipt();
        }
    }

    private Task<ScenarioLifecycleReceipt> GetCleanupTask()
    {
        lock (_cleanupLock)
        {
            return _cleanup ??= CleanupAsync();
        }
    }

    internal async Task WaitForRetainedCleanupAsync(CancellationToken cancellationToken)
    {
        Task? retainedCleanup;
        lock (_cleanupLock)
        {
            retainedCleanup = _retainedCleanupObservation;
        }

        if (retainedCleanup is not null)
        {
            await retainedCleanup.WaitAsync(cancellationToken);
        }
    }

    private async Task<ScenarioLifecycleReceipt> CleanupAsync()
    {
        await _cleanupGate.WaitAsync();
        try
        {
            return await ContinueCleanupAsync();
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    private async Task<ScenarioLifecycleReceipt> ContinueCleanupAsync()
    {
        while (_cleanupStage != ScenarioLifecycleCleanupStage.Complete)
        {
            if (_browserAcquisition is { IsTerminal: false })
            {
                return UpdateReceipt();
            }

            var resource = CurrentCleanupResource();
            if (resource is null)
            {
                await CompleteCurrentCleanupStageAsync();
                continue;
            }

            _cleanupCategories.Add(CurrentCleanupCategory());
            var rawDisposal = CaptureRawDisposal(resource);
            using var shutdown = new CancellationTokenSource(_shutdownTimeout);
            try
            {
                await rawDisposal.WaitAsync(shutdown.Token);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                _cleanupFailures++;
                _retainedCleanupObservation ??= ObserveRetainedCleanupAsync(_cleanupStage, rawDisposal);
                return UpdateReceipt();
            }
            catch (Exception)
            {
                _cleanupFailures++;
            }

            await CompleteCurrentCleanupStageAsync();
        }

        _databaseAbsent = await ConfirmAbsentAsync(_databaseObservation);
        return UpdateReceipt();
    }

    private async Task ObserveRetainedCleanupAsync(
        ScenarioLifecycleCleanupStage retainedStage,
        Task rawDisposal)
    {
        try
        {
            await rawDisposal;
        }
        catch (Exception)
        {
        }

        await _cleanupGate.WaitAsync();
        try
        {
            if (_cleanupStage != retainedStage)
            {
                return;
            }

            _retainedCleanupObservation = null;
            await CompleteCurrentCleanupStageAsync();
            await ContinueCleanupAsync();
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    private async Task CompleteCurrentCleanupStageAsync()
    {
        switch (_cleanupStage)
        {
            case ScenarioLifecycleCleanupStage.BrowserScenario:
                _browserScenario = null;
                break;
            case ScenarioLifecycleCleanupStage.BrowserRun:
                _browserRun = null;
                break;
            case ScenarioLifecycleCleanupStage.Expo:
                _expoAbsent = _expo is not null && await ConfirmAbsentAsync(_expo);
                _expo = null;
                break;
            case ScenarioLifecycleCleanupStage.Api:
                _apiAbsent = _api is not null && await ConfirmAbsentAsync(_api.Observation);
                _api = null;
                break;
            case ScenarioLifecycleCleanupStage.PostgreSql:
                _scenarioDatabase = null;
                break;
            case ScenarioLifecycleCleanupStage.ScenarioPaths:
                _scenarioPathsAbsent = _scenarioPaths is null || !Directory.Exists(_scenarioPaths.ScenarioDirectory);
                _scenarioPaths = null;
                break;
            case ScenarioLifecycleCleanupStage.Complete:
                return;
        }

        _cleanupStage++;
    }

    private IAsyncDisposable? CurrentCleanupResource() => _cleanupStage switch
    {
        ScenarioLifecycleCleanupStage.BrowserScenario => _browserScenario,
        ScenarioLifecycleCleanupStage.BrowserRun => _browserRun,
        ScenarioLifecycleCleanupStage.Expo => _expo,
        ScenarioLifecycleCleanupStage.Api => _api,
        ScenarioLifecycleCleanupStage.PostgreSql => _scenarioDatabase,
        ScenarioLifecycleCleanupStage.ScenarioPaths => _scenarioPaths,
        _ => null
    };

    private string CurrentCleanupCategory() => _cleanupStage switch
    {
        ScenarioLifecycleCleanupStage.BrowserScenario => BrowserScenarioCategory,
        ScenarioLifecycleCleanupStage.BrowserRun => BrowserRunCategory,
        ScenarioLifecycleCleanupStage.Expo => ExpoCategory,
        ScenarioLifecycleCleanupStage.Api => ApiCategory,
        ScenarioLifecycleCleanupStage.PostgreSql => PostgreSqlCategory,
        ScenarioLifecycleCleanupStage.ScenarioPaths => ScenarioPathsCategory,
        _ => throw new InvalidOperationException("E2E scenario cleanup has no active category.")
    };

    private static Task CaptureRawDisposal(IAsyncDisposable resource)
    {
        try
        {
            return resource.DisposeAsync().AsTask();
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    private ScenarioLifecycleReceipt UpdateReceipt()
    {
        Receipt = Receipt with
        {
            AcquiredCategories = _acquiredCategories.ToArray(),
            AttemptedCleanupCategories = _cleanupCategories.ToArray(),
            CleanupFailureCount = _cleanupFailures,
            DatabaseAbsent = _databaseAbsent,
            ApiAbsent = _apiAbsent,
            ExpoAbsent = _expoAbsent,
            ScenarioPathsAbsent = _scenarioPathsAbsent
        };
        return Receipt;
    }

    private async Task<T> AcquireBrowserResourceAsync<T>(
        ScenarioLifecycleAcquisitionStage stage,
        Func<Task<T>> start,
        Action<T> assign,
        string category,
        CancellationToken cancellationToken)
        where T : class, IAsyncDisposable
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_browserAcquisition is { IsTerminal: false })
        {
            throw new InvalidOperationException("E2E browser acquisition is already in progress.");
        }

        var rawTask = CaptureRawAcquisition(start);
        var acquisition = new ScenarioLifecycleAcquisition<T>(stage, rawTask);
        _browserAcquisition = acquisition;
        acquisition.Observer = ObserveBrowserAcquisitionAsync(acquisition, assign, category);

        try
        {
            var resource = await rawTask.WaitAsync(cancellationToken);
            await acquisition.Observer;
            return resource;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            acquisition.Abandon();
            throw;
        }
        catch
        {
            await acquisition.Observer;
            throw;
        }
    }

    private async Task ObserveBrowserAcquisitionAsync<T>(
        ScenarioLifecycleAcquisition<T> acquisition,
        Action<T> assign,
        string category)
        where T : class, IAsyncDisposable
    {
        T? resource = null;
        Exception? terminalFault = null;
        try
        {
            resource = await acquisition.RawTask;
        }
        catch (Exception exception)
        {
            terminalFault = exception;
        }

        await _cleanupGate.WaitAsync();
        try
        {
            if (terminalFault is null)
            {
                acquisition.Complete(resource!);
                assign(resource!);
                _acquiredCategories.Add(category);
            }
            else
            {
                acquisition.Fail(terminalFault);
            }

            if (CleanupWasRequested())
            {
                await ContinueCleanupAsync();
            }
        }
        finally
        {
            _cleanupGate.Release();
        }
    }

    private bool CleanupWasRequested()
    {
        lock (_cleanupLock)
        {
            return _cleanup is not null;
        }
    }

    private static Task<T> CaptureRawAcquisition<T>(Func<Task<T>> start)
    {
        try
        {
            return start();
        }
        catch (Exception exception)
        {
            return Task.FromException<T>(exception);
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

internal enum ScenarioLifecycleCleanupStage
{
    BrowserScenario,
    BrowserRun,
    Expo,
    Api,
    PostgreSql,
    ScenarioPaths,
    Complete
}

internal enum ScenarioLifecycleAcquisitionStage
{
    BrowserRun,
    BrowserScenario
}

internal enum ScenarioLifecycleAcquisitionOwnership
{
    Active,
    Abandoned,
    Lifecycle,
    CleanupOnly,
    Faulted
}

internal interface IScenarioLifecycleAcquisition
{
    ScenarioLifecycleAcquisitionStage Stage { get; }

    bool IsTerminal { get; }
}

internal sealed class ScenarioLifecycleAcquisition<T>(
    ScenarioLifecycleAcquisitionStage stage,
    Task<T> rawTask) : IScenarioLifecycleAcquisition
    where T : class, IAsyncDisposable
{
    private readonly object _sync = new();
    private ScenarioLifecycleAcquisitionOwnership _ownership = ScenarioLifecycleAcquisitionOwnership.Active;

    public ScenarioLifecycleAcquisitionStage Stage { get; } = stage;

    public bool IsTerminal
    {
        get
        {
            lock (_sync)
            {
                return _ownership is ScenarioLifecycleAcquisitionOwnership.Lifecycle or
                    ScenarioLifecycleAcquisitionOwnership.CleanupOnly or
                    ScenarioLifecycleAcquisitionOwnership.Faulted;
            }
        }
    }

    internal Task<T> RawTask { get; } = rawTask;

    internal Task Observer { get; set; } = Task.CompletedTask;

    internal T? Resource { get; private set; }

    internal Exception? TerminalFault { get; private set; }

    internal void Abandon()
    {
        lock (_sync)
        {
            _ownership = _ownership switch
            {
                ScenarioLifecycleAcquisitionOwnership.Active => ScenarioLifecycleAcquisitionOwnership.Abandoned,
                ScenarioLifecycleAcquisitionOwnership.Lifecycle => ScenarioLifecycleAcquisitionOwnership.CleanupOnly,
                _ => _ownership
            };
        }
    }

    internal void Complete(T resource)
    {
        lock (_sync)
        {
            Resource = resource;
            _ownership = _ownership == ScenarioLifecycleAcquisitionOwnership.Abandoned
                ? ScenarioLifecycleAcquisitionOwnership.CleanupOnly
                : ScenarioLifecycleAcquisitionOwnership.Lifecycle;
        }
    }

    internal void Fail(Exception exception)
    {
        lock (_sync)
        {
            TerminalFault = exception;
            _ownership = ScenarioLifecycleAcquisitionOwnership.Faulted;
        }
    }
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
            request.Options.Timeouts.BrowserActionMilliseconds)
        {
            RuntimeDirectory = browserRuntime
        }));
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

        return new BrowserScenarioResource(await BrowserScenarioLifecycleAdapter.CreateAsync(
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
