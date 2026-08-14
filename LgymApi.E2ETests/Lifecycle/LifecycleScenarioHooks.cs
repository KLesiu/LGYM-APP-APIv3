using System.Runtime.ExceptionServices;
using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Harness;
using Reqnroll;

namespace LgymApi.E2ETests.Lifecycle;

[Binding]
public sealed class LifecycleScenarioHooks
{
    internal const int ScenarioBeforeOrder = 100;
    internal const int FailureProjectionOrder = 700;
    internal const int ScenarioAfterOrder = 800;
    internal const int RunAfterOrder = 1000;

    private readonly ScenarioContext _scenarioContext;

    public LifecycleScenarioHooks(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [BeforeScenario("@Lifecycle", Order = ScenarioBeforeOrder)]
    public async Task BeforeLifecycleScenarioAsync()
    {
        var run = await LifecycleRunStateHolder.GetOrCreateAsync();
        var lease = await run.CreateScenarioAsync(CaseIdFor(_scenarioContext));
        _scenarioContext.Set(lease);
    }

    [AfterScenario("@Lifecycle", Order = FailureProjectionOrder)]
    public Task ProjectLifecycleFailureAsync()
    {
        if (_scenarioContext.TestError is not null)
        {
            LifecycleRunStateHolder.Get()?.RecordFailure();
        }

        return Task.CompletedTask;
    }

    [AfterScenario("@Lifecycle", Order = ScenarioAfterOrder)]
    public async Task AfterLifecycleScenarioAsync()
    {
        if (!_scenarioContext.TryGetValue<ScenarioLifecycleLease>(out var lease))
        {
            return;
        }

        var run = LifecycleRunStateHolder.Get();
        try
        {
            await lease.DisposeAsync();
            run?.RecordSuccessfulScenario(lease);
        }
        catch
        {
            run?.RecordFailure();
            throw;
        }
    }

    [AfterTestRun(Order = RunAfterOrder)]
    public static async Task AfterLifecycleRunAsync()
    {
        var run = LifecycleRunStateHolder.Take();
        if (run is not null)
        {
            await run.DisposeAsync();
        }
    }

    private static string CaseIdFor(ScenarioContext scenarioContext) => scenarioContext.ScenarioInfo.Title switch
    {
        "lifecycle-probe-a" => "lifecycle-probe-a",
        "lifecycle-probe-b" => "lifecycle-probe-b",
        _ => throw new InvalidOperationException("E2E lifecycle scenario has no canonical case identifier.")
    };
}

[Binding]
public sealed class LifecycleScenarioSteps
{
    private readonly ScenarioContext _scenarioContext;

    public LifecycleScenarioSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Then("the lifecycle stack is ready")]
    public void ThenTheLifecycleStackIsReady()
    {
        var lease = _scenarioContext.Get<ScenarioLifecycleLease>();

        Assert.Multiple(() =>
        {
            Assert.That(lease.Page, Is.Not.Null);
            Assert.That(lease.Receipt.AcquiredCategories, Is.EqualTo([
                "scenario-paths", "postgresql", "external-api-host", "expo", "browser-run", "browser-scenario"]));
            Assert.That(lease.Receipt.PreviousResourcesAbsent, Is.True);
            Assert.That(lease.Receipt.DatabaseIdentityDistinct, Is.True);
        });
    }

    [Then("the lifecycle browser storage is empty")]
    public void ThenTheLifecycleBrowserStorageIsEmpty()
    {
        var lease = _scenarioContext.Get<ScenarioLifecycleLease>();
        Assert.That(lease.Receipt.BrowserStorageEmpty, Is.True);
    }
}

internal sealed class LifecycleRunState : IAsyncDisposable
{
    private readonly LifecycleRunDirectoryLease _run;
    private readonly WebSourceRunLease _source;
    private readonly RealApiHostProofContext _api;
    private readonly CancellationTokenSource _provisioning;
    private readonly object _sync = new();
    private ScenarioLifecycleObservation? _previous;
    private bool _hasFailure;

    private LifecycleRunState(
        LifecycleRunDirectoryLease run,
        WebSourceRunLease source,
        RealApiHostProofContext api,
        CancellationTokenSource provisioning)
    {
        _run = run;
        _source = source;
        _api = api;
        _provisioning = provisioning;
    }

    internal static async Task<LifecycleRunState> CreateAsync()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, repositoryRoot);
        var provisioning = new CancellationTokenSource(TimeSpan.FromSeconds(options.Timeouts.TestSessionSeconds));
        LifecycleRunDirectoryLease? run = null;
        WebSourceRunLease? source = null;

        try
        {
            var api = await RealApiHostProofContext.CreateAsync(provisioning.Token);
            run = LifecycleRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
                api.RepositoryRoot,
                api.Options.Runtime.PrivateRunRoot,
                TimeSpan.FromSeconds(api.Options.Timeouts.ProcessShutdownSeconds)));
            var git = ApiRepositoryStateReader.ResolveGitExecutable();
            source = await WebSourceRunLease.CreateAsync(
                new WebSourceRunRequest(api.RepositoryRoot, api.Options, git, []),
                new WebSourceRunDependencies
                {
                    Stager = new WebSourceStager(git),
                    ToolResolver = new NodeNpmToolResolver(),
                    CommandRunner = new NodeNpmCommandRunner()
                },
                run,
                provisioning.Token);
            await source.EnsureInstalledAsync(provisioning.Token);
            return new LifecycleRunState(run, source, api, provisioning);
        }
        catch (Exception startupFailure)
        {
            var cleanupFailed = false;
            try
            {
                if (source is not null)
                {
                    await source.DisposeAsync();
                }

                if (run is not null)
                {
                    await run.FinalizeFailureAsync();
                }
            }
            catch
            {
                cleanupFailed = true;
            }
            finally
            {
                provisioning.Dispose();
            }

            if (cleanupFailed)
            {
                startupFailure.Data["LifecycleRunCleanupFailed"] = true;
            }

            ExceptionDispatchInfo.Capture(startupFailure).Throw();
            throw;
        }
    }

    internal async Task<ScenarioLifecycleLease> CreateScenarioAsync(string caseId)
    {
        ScenarioLifecycleObservation? previous;
        lock (_sync)
        {
            previous = _previous;
        }

        using var scenario = CancellationTokenSource.CreateLinkedTokenSource(_provisioning.Token);
        scenario.CancelAfter(TimeSpan.FromSeconds(_api.Options.Timeouts.ScenarioSeconds));
        return await ScenarioLifecycleLease.CreateAsync(
            new ScenarioLifecycleRequest(
                _run,
                _source,
                _api.Options,
                _api.Publication,
                _api.RepositoryRoot,
                caseId,
                previous),
            cancellationToken: scenario.Token);
    }

    internal void RecordSuccessfulScenario(ScenarioLifecycleLease lease)
    {
        lock (_sync)
        {
            _previous = lease.Observation;
        }
    }

    internal void RecordFailure()
    {
        lock (_sync)
        {
            _hasFailure = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Exception? primaryFailure = null;
        var finalizationFailed = false;
        try
        {
            await _source.DisposeAsync();
        }
        catch (Exception exception)
        {
            RecordFailure();
            primaryFailure = exception;
        }

        try
        {
            if (HasFailure())
            {
                await _run.FinalizeFailureAsync();
            }
            else
            {
                await _run.FinalizeSuccessAsync();
            }
        }
        catch (Exception exception)
        {
            primaryFailure ??= exception;
            finalizationFailed = true;
        }
        finally
        {
            _provisioning.Dispose();
        }

        if (primaryFailure is not null)
        {
            if (finalizationFailed)
            {
                primaryFailure.Data["LifecycleRunFinalizationFailed"] = true;
            }

            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }
    }

    private bool HasFailure()
    {
        lock (_sync)
        {
            return _hasFailure;
        }
    }
}

internal static class LifecycleRunStateHolder
{
    private static readonly object Sync = new();
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);
    private static LifecycleRunState? _state;

    internal static async Task<LifecycleRunState> GetOrCreateAsync()
    {
        await InitializationGate.WaitAsync();
        try
        {
            lock (Sync)
            {
                if (_state is not null)
                {
                    return _state;
                }
            }

            var state = await LifecycleRunState.CreateAsync();
            lock (Sync)
            {
                _state = state;
                return state;
            }
        }
        finally
        {
            InitializationGate.Release();
        }
    }

    internal static LifecycleRunState? Get()
    {
        lock (Sync)
        {
            return _state;
        }
    }

    internal static LifecycleRunState? Take()
    {
        lock (Sync)
        {
            var state = _state;
            _state = null;
            return state;
        }
    }
}
