using System.Diagnostics;
using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Harness;
using Microsoft.Playwright;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class ScenarioLifecycleBrowserAcquisitionTests
{
    [Test]
    public async Task Cancellation_before_browser_start_does_not_create_a_browser_owner()
    {
        await using var fixture = await BrowserAcquisitionFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var dependencies = new ControlledBrowserAcquisitionDependencies(
            ControlledAcquisitionStage.None,
            LateCompletion.Success,
            cancellation.Cancel);
        ScenarioLifecycleLease? lease = null;

        try
        {
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                lease = await ScenarioLifecycleLease.CreateAsync(
                    fixture.CreateRequest("browser-canceled-before-start"),
                    dependencies,
                    cancellation.Token));
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(dependencies.BrowserRunCreateCount, Is.Zero);
            Assert.That(dependencies.BrowserScenarioCreateCount, Is.Zero);
            Assert.That(dependencies.BrowserRunDisposeCount, Is.Zero);
            Assert.That(dependencies.BrowserScenarioDisposeCount, Is.Zero);
        });
    }

    [TestCase(ControlledAcquisitionStage.BrowserRun, LateCompletion.Success)]
    [TestCase(ControlledAcquisitionStage.BrowserRun, LateCompletion.Fault)]
    [TestCase(ControlledAcquisitionStage.Context, LateCompletion.Success)]
    [TestCase(ControlledAcquisitionStage.Context, LateCompletion.Fault)]
    [TestCase(ControlledAcquisitionStage.Page, LateCompletion.Success)]
    [TestCase(ControlledAcquisitionStage.Page, LateCompletion.Fault)]
    public async Task Canceled_browser_acquisition_remains_owned_until_late_terminal_completion(
        ControlledAcquisitionStage stage,
        LateCompletion completion)
    {
        await using var fixture = await BrowserAcquisitionFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var dependencies = new ControlledBrowserAcquisitionDependencies(stage, completion);
        var caseId = $"retained-{stage}-{completion}".ToLowerInvariant();
        var creation = ScenarioLifecycleLease.CreateAsync(
            fixture.CreateRequest(caseId),
            dependencies,
            cancellation.Token);

        try
        {
            await dependencies.ControlledStageStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var startedAt = Stopwatch.GetTimestamp();
            cancellation.Cancel();

            var exception = Assert.CatchAsync<OperationCanceledException>(async () =>
                await creation.WaitAsync(TimeSpan.FromSeconds(1)));
            var receipt = (ScenarioLifecycleReceipt)exception!.Data[nameof(ScenarioLifecycleReceipt)]!;

            Assert.Multiple(() =>
            {
                Assert.That(Stopwatch.GetElapsedTime(startedAt), Is.LessThan(TimeSpan.FromSeconds(1)));
                Assert.That(receipt.AttemptedCleanupCategories, Is.Empty);
                Assert.That(dependencies.CleanupEvents, Is.Empty);
                Assert.That(fixture.ScenarioWasRemoved(caseId), Is.False);
            });
        }
        finally
        {
            dependencies.ResolveControlledStage();
            await DrainCreationAsync(creation);
            await fixture.WaitUntilScenarioRemovedAsync(caseId);
        }

        Assert.Multiple(() =>
        {
            Assert.That(dependencies.BrowserRunCreateCount, Is.EqualTo(1));
            Assert.That(dependencies.BrowserRunDisposeCount, Is.EqualTo(
                stage == ControlledAcquisitionStage.BrowserRun && completion == LateCompletion.Fault ? 0 : 1));
            Assert.That(dependencies.BrowserScenarioCreateCount, Is.EqualTo(
                stage == ControlledAcquisitionStage.BrowserRun ? 0 : 1));
            Assert.That(dependencies.BrowserScenarioDisposeCount, Is.EqualTo(
                stage != ControlledAcquisitionStage.BrowserRun && completion == LateCompletion.Success ? 1 : 0));
            Assert.That(dependencies.PendingTaskCount, Is.Zero);
            Assert.That(dependencies.CurrentCleanupCount, Is.Zero);
            Assert.That(dependencies.MaximumCleanupCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Scenario_deadline_bounds_the_caller_without_releasing_the_unresolved_browser_owner()
    {
        await using var fixture = await BrowserAcquisitionFixture.CreateAsync();
        var dependencies = new ControlledBrowserAcquisitionDependencies(
            ControlledAcquisitionStage.BrowserRun,
            LateCompletion.Success);
        const string caseId = "browser-scenario-deadline";
        var startedAt = Stopwatch.GetTimestamp();
        var creation = ScenarioLifecycleLease.CreateAsync(
            fixture.CreateRequest(caseId, scenarioSeconds: 1),
            dependencies);

        try
        {
            await dependencies.ControlledStageStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await creation.WaitAsync(TimeSpan.FromSeconds(3)));

            Assert.Multiple(() =>
            {
                Assert.That(Stopwatch.GetElapsedTime(startedAt), Is.LessThan(TimeSpan.FromSeconds(3)));
                Assert.That(dependencies.CleanupEvents, Is.Empty);
                Assert.That(fixture.ScenarioWasRemoved(caseId), Is.False);
            });
        }
        finally
        {
            dependencies.ResolveControlledStage();
            await DrainCreationAsync(creation);
            await fixture.WaitUntilScenarioRemovedAsync(caseId);
        }

        Assert.That(dependencies.PendingTaskCount, Is.Zero);
    }

    [Test]
    public async Task Acquisition_observer_and_failed_start_cleanup_race_dispose_each_resource_once()
    {
        await using var fixture = await BrowserAcquisitionFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var dependencies = new ControlledBrowserAcquisitionDependencies(
            ControlledAcquisitionStage.BrowserRun,
            LateCompletion.Success);
        const string caseId = "browser-observer-cleanup-race";
        var creation = ScenarioLifecycleLease.CreateAsync(
            fixture.CreateRequest(caseId),
            dependencies,
            cancellation.Token);

        await dependencies.ControlledStageStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var race = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancel = CancelAfterSignalAsync(race.Task, cancellation);
        var complete = CompleteAfterSignalAsync(race.Task, dependencies);
        race.SetResult();
        await Task.WhenAll(cancel, complete);
        await DrainCreationAsync(creation);
        await fixture.WaitUntilScenarioRemovedAsync(caseId);

        Assert.Multiple(() =>
        {
            Assert.That(dependencies.BrowserRunCreateCount, Is.EqualTo(1));
            Assert.That(dependencies.BrowserRunDisposeCount, Is.EqualTo(1));
            Assert.That(dependencies.BrowserScenarioCreateCount, Is.LessThanOrEqualTo(1));
            Assert.That(dependencies.BrowserScenarioDisposeCount, Is.EqualTo(dependencies.BrowserScenarioCreateCount));
            Assert.That(dependencies.PendingTaskCount, Is.Zero);
            Assert.That(dependencies.CurrentCleanupCount, Is.Zero);
            Assert.That(dependencies.MaximumCleanupCount, Is.EqualTo(1));
        });
    }

    private static async Task CancelAfterSignalAsync(Task signal, CancellationTokenSource cancellation)
    {
        await signal;
        cancellation.Cancel();
    }

    private static async Task CompleteAfterSignalAsync(
        Task signal,
        ControlledBrowserAcquisitionDependencies dependencies)
    {
        await signal;
        await Task.Yield();
        dependencies.ResolveControlledStage();
    }

    private static async Task DrainCreationAsync(Task<ScenarioLifecycleLease> creation)
    {
        try
        {
            var lease = await creation.WaitAsync(TimeSpan.FromSeconds(3));
            await lease.DisposeAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException exception) when (exception.Message == ControlledBrowserAcquisitionDependencies.FaultMessage)
        {
        }
    }

    public enum ControlledAcquisitionStage
    {
        None,
        BrowserRun,
        Context,
        Page
    }

    public enum LateCompletion
    {
        Success,
        Fault
    }

    private sealed class ControlledBrowserAcquisitionDependencies(
        ControlledAcquisitionStage controlledStage,
        LateCompletion lateCompletion,
        Action? afterExpoStart = null) : IScenarioLifecycleDependencies
    {
        internal const string FaultMessage = "late browser acquisition canary";
        private readonly TaskCompletionSource _controlledCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<IScenarioLifecycleBrowserRun>? _browserRunTask;
        private Task<IScenarioLifecycleBrowserScenario>? _browserScenarioTask;
        private int _currentCleanupCount;

        internal TaskCompletionSource ControlledStageStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<string> CleanupEvents { get; } = [];
        internal int BrowserRunCreateCount { get; private set; }
        internal int BrowserScenarioCreateCount { get; private set; }
        internal int BrowserRunDisposeCount { get; private set; }
        internal int BrowserScenarioDisposeCount { get; private set; }
        internal int MaximumCleanupCount { get; private set; }
        internal int CurrentCleanupCount => Volatile.Read(ref _currentCleanupCount);
        internal int PendingTaskCount =>
            (_browserRunTask is { IsCompleted: false } ? 1 : 0) +
            (_browserScenarioTask is { IsCompleted: false } ? 1 : 0);

        public Task<ScenarioDatabaseOwnership> StartDatabaseAsync(CancellationToken cancellationToken)
        {
            var database = new RecordingDatabase();
            return Task.FromResult(new ScenarioDatabaseOwnership(
                database,
                new ScenarioResourceObservation(ScenarioResourceIdentity.Create(), database.ConfirmAbsentAsync)));
        }

        public Task<IScenarioLifecycleApiHost> StartApiHostAsync(
            IApiHostDatabaseLease database,
            LifecycleComponentDirectoryLease apiRuntime,
            ScenarioLifecycleRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<IScenarioLifecycleApiHost>(new RecordingApiHost(database, this));

        public Task<IScenarioLifecycleExpo> StartExpoAsync(
            LifecycleComponentDirectoryLease webRuntime,
            Uri apiBaseAddress,
            ScenarioLifecycleRequest request,
            CancellationToken cancellationToken)
        {
            afterExpoStart?.Invoke();
            return Task.FromResult<IScenarioLifecycleExpo>(new RecordingExpo(this));
        }

        public Task<IScenarioLifecycleBrowserRun> StartBrowserRunAsync(
            LifecycleComponentDirectoryLease browserRuntime,
            ScenarioLifecycleRequest request,
            CancellationToken cancellationToken)
        {
            BrowserRunCreateCount++;
            _browserRunTask = StartBrowserRunCoreAsync();
            return _browserRunTask;
        }

        public Task<IScenarioLifecycleBrowserScenario> StartBrowserScenarioAsync(
            IScenarioLifecycleBrowserRun browserRun,
            ScenarioLifecycleRequest request,
            CancellationToken cancellationToken)
        {
            BrowserScenarioCreateCount++;
            _browserScenarioTask = StartBrowserScenarioCoreAsync();
            return _browserScenarioTask;
        }

        internal void ResolveControlledStage() => _controlledCompletion.TrySetResult();

        private async Task<IScenarioLifecycleBrowserRun> StartBrowserRunCoreAsync()
        {
            await WaitForControlledStageAsync(ControlledAcquisitionStage.BrowserRun);
            return new RecordingBrowserRun(this);
        }

        private async Task<IScenarioLifecycleBrowserScenario> StartBrowserScenarioCoreAsync()
        {
            await WaitForControlledStageAsync(ControlledAcquisitionStage.Context);
            await WaitForControlledStageAsync(ControlledAcquisitionStage.Page);
            return new RecordingBrowserScenario(this);
        }

        private async Task WaitForControlledStageAsync(ControlledAcquisitionStage stage)
        {
            if (controlledStage != stage)
            {
                return;
            }

            ControlledStageStarted.TrySetResult();
            await _controlledCompletion.Task;
            if (lateCompletion == LateCompletion.Fault)
            {
                throw new InvalidOperationException(FaultMessage);
            }
        }

        private async ValueTask RecordCleanupAsync(string category, Func<ValueTask>? child = null)
        {
            var concurrent = Interlocked.Increment(ref _currentCleanupCount);
            MaximumCleanupCount = Math.Max(MaximumCleanupCount, concurrent);
            try
            {
                CleanupEvents.Add(category);
                if (child is not null)
                {
                    await child();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _currentCleanupCount);
            }
        }

        private sealed class RecordingDatabase : IApiHostDatabaseLease
        {
            private bool _absent;

            public string ConnectionString => "safe-test-connection";

            public ValueTask DisposeAsync()
            {
                _absent = true;
                return ValueTask.CompletedTask;
            }

            internal Task<bool> ConfirmAbsentAsync() => Task.FromResult(_absent);
        }

        private sealed class RecordingApiHost(
            IApiHostDatabaseLease database,
            ControlledBrowserAcquisitionDependencies owner) : IScenarioLifecycleApiHost
        {
            public Uri BaseAddress { get; } = new("http://127.0.0.1:40123/");

            public ExternalApiHostObservation Observation { get; } = new(
                ScenarioResourceIdentity.Create(),
                () => new ExternalApiHostCleanupReceipt(true, true, true, [], 0));

            public ValueTask DisposeAsync() => owner.RecordCleanupAsync("external-api-host", database.DisposeAsync);
        }

        private sealed class RecordingExpo(ControlledBrowserAcquisitionDependencies owner) : IScenarioLifecycleExpo
        {
            private bool _absent;

            public ExpoWebIdentity Identity { get; } = ExpoWebIdentity.Create();

            public async ValueTask DisposeAsync()
            {
                await owner.RecordCleanupAsync("expo");
                _absent = true;
            }

            public Task<bool> ConfirmAbsentAsync() => Task.FromResult(_absent);
        }

        private sealed class RecordingBrowserRun(ControlledBrowserAcquisitionDependencies owner)
            : IScenarioLifecycleBrowserRun
        {
            public async ValueTask DisposeAsync()
            {
                owner.BrowserRunDisposeCount++;
                await owner.RecordCleanupAsync("browser-run");
            }
        }

        private sealed class RecordingBrowserScenario(ControlledBrowserAcquisitionDependencies owner)
            : IScenarioLifecycleBrowserScenario
        {
            public IPage Page => null!;

            public async ValueTask DisposeAsync()
            {
                owner.BrowserScenarioDisposeCount++;
                await owner.RecordCleanupAsync("browser-scenario");
            }

            public Task<bool> ConfirmStorageIsEmptyAsync() => Task.FromResult(true);
        }
    }

    private sealed class BrowserAcquisitionFixture(string root) : IAsyncDisposable
    {
        internal static Task<BrowserAcquisitionFixture> CreateAsync() => Task.FromResult(
            new BrowserAcquisitionFixture(Directory.CreateTempSubdirectory("lgym-e2e-browser-acquisition-").FullName));

        internal ScenarioLifecycleRequest CreateRequest(string caseId, int scenarioSeconds = 5)
        {
            var options = new E2EOptions
            {
                Runtime = new E2ERuntimeOptions { PrivateRunRoot = ".e2e-private/runs" },
                Timeouts = new E2ETimeoutsOptions
                {
                    ProcessShutdownSeconds = 1,
                    ScenarioSeconds = scenarioSeconds,
                    BrowserActionMilliseconds = 100
                }
            };
            var run = LifecycleRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
                root,
                options.Runtime.PrivateRunRoot,
                TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds)));
            return new ScenarioLifecycleRequest(run, null, options, null!, root, caseId);
        }

        internal bool ScenarioWasRemoved(string caseId)
        {
            var runsRoot = Path.Combine(root, ".e2e-private", "runs");
            return !Directory.Exists(runsRoot) || !Directory
                .EnumerateDirectories(runsRoot, "scenarios", SearchOption.AllDirectories)
                .SelectMany(directory => Directory.EnumerateDirectories(directory, caseId))
                .Any();
        }

        internal async Task WaitUntilScenarioRemovedAsync(string caseId)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (ScenarioWasRemoved(caseId))
                {
                    return;
                }

                await Task.Delay(50);
            }

            Assert.Fail("The retained browser acquisition cleanup did not drain.");
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
