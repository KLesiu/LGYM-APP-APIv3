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
        var artifactOwner = lease.GetFailureArtifactOwner();
        try
        {
            await lease.DisposeAsync();
            if (_scenarioContext.TestError is null)
            {
                run?.RecordSuccessfulScenario(CaseIdFor(_scenarioContext), lease);
            }
            else if (run is not null)
            {
                run.RecordFailedScenario(lease);
                await run.FinalizeFailedScenarioAsync(lease, artifactOwner);
            }
        }
        catch
        {
            if (run is not null)
            {
                run.RecordFailedScenario(lease);
                await run.FinalizeFailedScenarioAsync(lease, artifactOwner);
            }

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
    private readonly List<FinalLifecycleScenarioReceipt> _completedScenarios = [];
    private ScenarioLifecycleLease? _previous;
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
        ScenarioLifecycleLease? previous;
        lock (_sync)
        {
            previous = _previous;
        }

        using var scenario = CancellationTokenSource.CreateLinkedTokenSource(_provisioning.Token);
        scenario.CancelAfter(TimeSpan.FromSeconds(_api.Options.Timeouts.ScenarioSeconds));
        if (previous is not null)
        {
            await previous.WaitForRetainedCleanupAsync(scenario.Token);
        }

        return await ScenarioLifecycleLease.CreateAsync(
            new ScenarioLifecycleRequest(
                _run,
                _source,
                _api.Options,
                _api.Publication,
                _api.RepositoryRoot,
                caseId,
                previous?.Observation),
            cancellationToken: scenario.Token);
    }

    internal void RecordSuccessfulScenario(string caseId, ScenarioLifecycleLease lease)
    {
        var receipt = lease.Receipt;
        var readyStack = receipt.AcquiredCategories.SequenceEqual([
            "scenario-paths", "postgresql", "external-api-host", "expo", "browser-run", "browser-scenario"], StringComparer.Ordinal);
        lock (_sync)
        {
            _previous = lease;
            _completedScenarios.Add(new FinalLifecycleScenarioReceipt(
                caseId,
                receipt.AcquiredCategories,
                receipt.AttemptedCleanupCategories,
                receipt.CleanupFailureCount,
                receipt.DatabaseIdentityDistinct,
                readyStack,
                readyStack,
                readyStack,
                readyStack,
                receipt.PreviousResourcesAbsent,
                receipt.BrowserStorageEmpty,
                receipt.DatabaseAbsent,
                receipt.ApiAbsent,
                receipt.ExpoAbsent,
                receipt.ScenarioPathsAbsent));
        }
    }

    internal void RecordFailure()
    {
        lock (_sync)
        {
            _hasFailure = true;
        }
    }

    internal void RecordFailedScenario(ScenarioLifecycleLease lease)
    {
        lock (_sync)
        {
            _previous = lease;
            _hasFailure = true;
        }
    }

    internal Task FinalizeFailedScenarioAsync(
        ScenarioLifecycleLease lease,
        LifecycleScenarioDirectoryLease artifactOwner) => lease.FinalizeFailureAsync(
        artifactOwner,
        new ScenarioFailureArtifactWriter(_api.Publication.Receipt));

    public async ValueTask DisposeAsync()
    {
        Exception? primaryFailure = null;
        var finalizationFailed = false;
        try
        {
            await WaitForRetainedScenarioCleanupAsync();
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
                WriteSuccessReceipt();
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

    private async Task WaitForRetainedScenarioCleanupAsync()
    {
        ScenarioLifecycleLease? previous;
        lock (_sync)
        {
            previous = _previous;
        }

        if (previous is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(_api.Options.Timeouts.ProcessShutdownSeconds));
        await previous.WaitForRetainedCleanupAsync(timeout.Token);
    }

    private bool HasFailure()
    {
        lock (_sync)
        {
            return _hasFailure;
        }
    }

    private void WriteSuccessReceipt()
    {
        FinalLifecycleScenarioReceipt[] scenarios;
        lock (_sync)
        {
            scenarios = _completedScenarios.ToArray();
        }

        LifecycleEvidenceReceiptWriter.Write(new FinalLifecycleRunReceipt(
            FinalLifecycleEvidenceManifest.LifecycleReceiptSchema,
            _api.Publication.Receipt.ApiRepositoryHeadSha,
            _api.Publication.Receipt.RepositoryIsDirty,
            scenarios.Length,
            true,
            !Directory.Exists(_run.RunDirectory),
            !Directory.Exists(_run.RunDirectory),
            scenarios));
    }
}

internal static class LifecycleEvidenceReceiptWriter
{
    internal const string ReceiptPathEnvironmentVariable = "HARNESS_ONLY_LIFECYCLE_RECEIPT_PATH";

    internal static void Write(FinalLifecycleRunReceipt receipt)
    {
        var configuredPath = Environment.GetEnvironmentVariable(ReceiptPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

        var repositoryRoot = RepositoryRoot.Find();
        var testResultsRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "LgymApi.E2ETests", "TestResults"));
        var receiptPath = Path.GetFullPath(configuredPath);
        var relativePath = Path.GetRelativePath(testResultsRoot, receiptPath);
        if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Lifecycle evidence receipt path is invalid.");
        }

        var serialized = System.Text.Json.JsonSerializer.Serialize(receipt, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
        File.WriteAllText(receiptPath, serialized);
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
