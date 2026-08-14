using LgymApi.E2ETests.Configuration;
using LgymApi.E2ETests.Harness;
using Microsoft.Playwright;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class ScenarioLifecycleLeaseTests
{
    [Test]
    public async Task ScenarioLifecycleLease_acquires_the_complete_stack_in_order_and_cleans_it_in_reverse()
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        var dependencies = new RecordingScenarioLifecycleDependencies();
        var lease = await ScenarioLifecycleLease.CreateAsync(fixture.CreateRequest("scenario-lifecycle-success"), dependencies);

        await Task.WhenAll(lease.DisposeAsync().AsTask(), lease.DisposeAsync().AsTask());

        Assert.Multiple(() =>
        {
            Assert.That(lease.Receipt.AcquiredCategories, Is.EqualTo([
                "scenario-paths", "postgresql", "external-api-host", "expo", "browser-run", "browser-scenario"]));
            Assert.That(dependencies.AcquisitionEvents, Is.EqualTo([
                "postgresql", "api-health", "api-database-ready", "expo", "browser-executable", "browser-context", "page"]));
            Assert.That(lease.Receipt.AttemptedCleanupCategories, Is.EqualTo([
                "browser-scenario", "browser-run", "expo", "external-api-host", "scenario-paths"]));
            Assert.That(dependencies.CleanupEvents, Is.EqualTo([
                "browser-scenario", "browser-run", "expo", "external-api-host", "postgresql"]));
            Assert.That(dependencies.Database.DisposeCount, Is.EqualTo(1));
            Assert.That(lease.Receipt.CleanupFailureCount, Is.Zero);
            Assert.That(lease.Receipt.DatabaseIdentityDistinct, Is.True);
            Assert.That(lease.Receipt.PreviousResourcesAbsent, Is.True);
            Assert.That(lease.Receipt.BrowserStorageEmpty, Is.True);
            Assert.That(lease.Receipt.DatabaseAbsent, Is.True);
            Assert.That(lease.Receipt.ApiAbsent, Is.True);
            Assert.That(lease.Receipt.ExpoAbsent, Is.True);
            Assert.That(lease.Receipt.ScenarioPathsAbsent, Is.True);
        });
    }

    [TestCase(ScenarioLifecycleFailureStage.PostgreSql, new[] { "scenario-paths" })]
    [TestCase(ScenarioLifecycleFailureStage.ApiStart, new[] { "scenario-paths" })]
    [TestCase(ScenarioLifecycleFailureStage.ApiHealth, new[] { "scenario-paths" })]
    [TestCase(ScenarioLifecycleFailureStage.ApiDatabaseReadiness, new[] { "scenario-paths" })]
    [TestCase(ScenarioLifecycleFailureStage.Expo, new[] { "external-api-host", "scenario-paths" })]
    [TestCase(ScenarioLifecycleFailureStage.BrowserExecutable, new[] { "expo", "external-api-host", "scenario-paths" })]
    [TestCase(ScenarioLifecycleFailureStage.BrowserContext, new[] { "browser-run", "expo", "external-api-host", "scenario-paths" })]
    [TestCase(ScenarioLifecycleFailureStage.BrowserPage, new[] { "browser-run", "expo", "external-api-host", "scenario-paths" })]
    public async Task ScenarioLifecycleLease_partial_acquisition_cleans_only_acquired_owners(
        ScenarioLifecycleFailureStage failureStage,
        string[] expectedCleanup)
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        var dependencies = new RecordingScenarioLifecycleDependencies(failureStage);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ScenarioLifecycleLease.CreateAsync(fixture.CreateRequest("scenario-lifecycle-failure"), dependencies));
        var receipt = (ScenarioLifecycleReceipt)exception!.Data[nameof(ScenarioLifecycleReceipt)]!;

        Assert.Multiple(() =>
        {
            Assert.That(receipt.AttemptedCleanupCategories, Is.EqualTo(expectedCleanup));
            Assert.That(dependencies.Database.DisposeCount, Is.LessThanOrEqualTo(1));
        });
    }

    [TestCase("browser-scenario")]
    [TestCase("browser-run")]
    [TestCase("expo")]
    [TestCase("external-api-host")]
    public async Task ScenarioLifecycleLease_continues_reverse_cleanup_after_each_cleanup_failure(string failingCategory)
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        var dependencies = new RecordingScenarioLifecycleDependencies(cleanupFailure: failingCategory);
        var lease = await ScenarioLifecycleLease.CreateAsync(fixture.CreateRequest("scenario-lifecycle-cleanup"), dependencies);

        var exception = Assert.ThrowsAsync<ScenarioLifecycleCleanupException>(async () => await lease.DisposeAsync());
        var repeated = Assert.ThrowsAsync<ScenarioLifecycleCleanupException>(async () => await lease.DisposeAsync());

        Assert.Multiple(() =>
        {
            Assert.That(lease.Receipt.AttemptedCleanupCategories, Is.EqualTo([
                "browser-scenario", "browser-run", "expo", "external-api-host", "scenario-paths"]));
            Assert.That(dependencies.CleanupEvents, Is.EqualTo([
                "browser-scenario", "browser-run", "expo", "external-api-host", "postgresql"]));
            Assert.That(exception!.Receipt.CleanupFailureCount, Is.EqualTo(1));
            Assert.That(repeated!.Receipt, Is.EqualTo(exception.Receipt));
            Assert.That(dependencies.Database.DisposeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ScenarioLifecycleLease_records_a_final_scenario_path_cleanup_failure_after_all_owned_resources()
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        var dependencies = new RecordingScenarioLifecycleDependencies();
        var lease = await ScenarioLifecycleLease.CreateAsync(
            fixture.CreateRequest("scenario-lifecycle-path-cleanup", cleaner: new FailingRunDirectoryCleaner()),
            dependencies);

        var exception = Assert.ThrowsAsync<ScenarioLifecycleCleanupException>(async () => await lease.DisposeAsync());

        Assert.Multiple(() =>
        {
            Assert.That(lease.Receipt.AttemptedCleanupCategories, Is.EqualTo([
                "browser-scenario", "browser-run", "expo", "external-api-host", "scenario-paths"]));
            Assert.That(dependencies.CleanupEvents, Is.EqualTo([
                "browser-scenario", "browser-run", "expo", "external-api-host", "postgresql"]));
            Assert.That(exception!.Receipt.CleanupFailureCount, Is.EqualTo(1));
            Assert.That(lease.Receipt.ScenarioPathsAbsent, Is.False);
        });
    }

    [Test]
    public async Task ScenarioLifecycleLease_requires_prior_absence_and_records_distinct_safe_identities()
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        var firstDependencies = new RecordingScenarioLifecycleDependencies();
        var first = await ScenarioLifecycleLease.CreateAsync(fixture.CreateRequest("scenario-lifecycle-first"), firstDependencies);
        await first.DisposeAsync();

        var secondDependencies = new RecordingScenarioLifecycleDependencies();
        var second = await ScenarioLifecycleLease.CreateAsync(
            fixture.CreateRequest("scenario-lifecycle-second", first.Observation),
            secondDependencies);
        await second.DisposeAsync();

        var secondAbsent = await second.Observation.ConfirmAbsentAsync();
        Assert.Multiple(() =>
        {
            Assert.That(second.Receipt.PreviousResourcesAbsent, Is.True);
            Assert.That(second.Receipt.DatabaseIdentityDistinct, Is.True);
            Assert.That(secondAbsent, Is.True);
        });
    }

    [Test]
    public async Task ScenarioLifecycleLease_real_stack_returns_an_accessible_Expo_page_after_both_API_gates()
    {
        using var scenario = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var api = await RealApiHostProofContext.CreateAsync(scenario.Token);
        var git = ApiRepositoryStateReader.ResolveGitExecutable();
        await using var run = LifecycleRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            api.RepositoryRoot,
            api.Options.Runtime.PrivateRunRoot,
            TimeSpan.FromSeconds(api.Options.Timeouts.ProcessShutdownSeconds)));
        await using var source = await WebSourceRunLease.CreateAsync(
            new WebSourceRunRequest(api.RepositoryRoot, api.Options, git, []),
            new WebSourceRunDependencies
            {
                Stager = new WebSourceStager(git),
                ToolResolver = new NodeNpmToolResolver(),
                CommandRunner = new NodeNpmCommandRunner()
            },
            run,
            scenario.Token);
        await source.EnsureInstalledAsync(scenario.Token);
        await using var lease = await ScenarioLifecycleLease.CreateAsync(
            new ScenarioLifecycleRequest(
                run,
                source,
                api.Options,
                api.Publication,
                api.RepositoryRoot,
                "scenario-lifecycle-real"),
            cancellationToken: scenario.Token);

        var response = await lease.Page.GotoAsync("/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Commit,
            Timeout = api.Options.Timeouts.BrowserActionMilliseconds
        });

        Assert.Multiple(() =>
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Status, Is.LessThan(400));
            Assert.That(lease.Receipt.BrowserStorageEmpty, Is.True);
            Assert.That(lease.Receipt.AcquiredCategories, Is.EqualTo([
                "scenario-paths", "postgresql", "external-api-host", "expo", "browser-run", "browser-scenario"]));
        });
    }

    public enum ScenarioLifecycleFailureStage
    {
        None,
        PostgreSql,
        ApiStart,
        ApiHealth,
        ApiDatabaseReadiness,
        Expo,
        BrowserExecutable,
        BrowserContext,
        BrowserPage
    }

    private sealed class RecordingScenarioLifecycleDependencies(
        ScenarioLifecycleFailureStage failureStage = ScenarioLifecycleFailureStage.None,
        string? cleanupFailure = null) : IScenarioLifecycleDependencies
    {
        internal List<string> AcquisitionEvents { get; } = [];
        internal List<string> CleanupEvents { get; } = [];
        internal RecordingDatabase Database { get; } = new();

        public Task<ScenarioDatabaseOwnership> StartDatabaseAsync(CancellationToken cancellationToken)
        {
            AcquisitionEvents.Add("postgresql");
            if (failureStage == ScenarioLifecycleFailureStage.PostgreSql)
            {
                throw new InvalidOperationException("private lifecycle canary");
            }

            return Task.FromResult(new ScenarioDatabaseOwnership(
                Database,
                new ScenarioResourceObservation(ScenarioResourceIdentity.Create(), Database.ConfirmAbsentAsync)));
        }

        public async Task<IScenarioLifecycleApiHost> StartApiHostAsync(
            IApiHostDatabaseLease database,
            LifecycleComponentDirectoryLease apiRuntime,
            ScenarioLifecycleRequest request,
            CancellationToken cancellationToken)
        {
            AcquisitionEvents.Add("api-health");
            if (failureStage is ScenarioLifecycleFailureStage.ApiStart or ScenarioLifecycleFailureStage.ApiHealth)
            {
                await database.DisposeAsync();
                throw new InvalidOperationException("private lifecycle canary");
            }

            AcquisitionEvents.Add("api-database-ready");
            if (failureStage == ScenarioLifecycleFailureStage.ApiDatabaseReadiness)
            {
                await database.DisposeAsync();
                throw new InvalidOperationException("private lifecycle canary");
            }

            return new RecordingApiHost(database, CleanupEvents, cleanupFailure == "external-api-host");
        }

        public Task<IScenarioLifecycleExpo> StartExpoAsync(
            LifecycleComponentDirectoryLease webRuntime,
            Uri apiBaseAddress,
            ScenarioLifecycleRequest request,
            CancellationToken cancellationToken)
        {
            AcquisitionEvents.Add("expo");
            if (failureStage == ScenarioLifecycleFailureStage.Expo)
            {
                throw new InvalidOperationException("private lifecycle canary");
            }

            return Task.FromResult<IScenarioLifecycleExpo>(new RecordingExpo(CleanupEvents, cleanupFailure == "expo"));
        }

        public Task<IScenarioLifecycleBrowserRun> StartBrowserRunAsync(
            LifecycleComponentDirectoryLease browserRuntime,
            ScenarioLifecycleRequest request,
            CancellationToken cancellationToken)
        {
            AcquisitionEvents.Add("browser-executable");
            if (failureStage == ScenarioLifecycleFailureStage.BrowserExecutable)
            {
                throw new InvalidOperationException("private lifecycle canary");
            }

            return Task.FromResult<IScenarioLifecycleBrowserRun>(new RecordingBrowserRun(
                CleanupEvents,
                cleanupFailure == "browser-run"));
        }

        public Task<IScenarioLifecycleBrowserScenario> StartBrowserScenarioAsync(
            IScenarioLifecycleBrowserRun browserRun,
            ScenarioLifecycleRequest request,
            CancellationToken cancellationToken)
        {
            AcquisitionEvents.Add("browser-context");
            if (failureStage == ScenarioLifecycleFailureStage.BrowserContext)
            {
                throw new InvalidOperationException("private lifecycle canary");
            }

            AcquisitionEvents.Add("page");
            if (failureStage == ScenarioLifecycleFailureStage.BrowserPage)
            {
                throw new InvalidOperationException("private lifecycle canary");
            }

            return Task.FromResult<IScenarioLifecycleBrowserScenario>(new RecordingBrowserScenario(
                CleanupEvents,
                cleanupFailure == "browser-scenario"));
        }
    }

    private sealed class RecordingDatabase : IApiHostDatabaseLease
    {
        internal int DisposeCount { get; private set; }
        internal bool IsAbsent { get; private set; }

        public string ConnectionString => "safe-test-connection";

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            IsAbsent = true;
            return ValueTask.CompletedTask;
        }

        internal Task<bool> ConfirmAbsentAsync() => Task.FromResult(IsAbsent);
    }

    private sealed class RecordingApiHost(
        IApiHostDatabaseLease database,
        ICollection<string> cleanupEvents,
        bool fails) : IScenarioLifecycleApiHost
    {
        public Uri BaseAddress { get; } = new("http://127.0.0.1:40123/");

        public ExternalApiHostObservation Observation { get; } = new(
            ScenarioResourceIdentity.Create(),
            () => new ExternalApiHostCleanupReceipt(true, true, true, [], 0));

        public async ValueTask DisposeAsync()
        {
            cleanupEvents.Add("external-api-host");
            await database.DisposeAsync();
            cleanupEvents.Add("postgresql");
            if (fails)
            {
                throw new IOException("private lifecycle canary");
            }
        }
    }

    private sealed class RecordingExpo(ICollection<string> cleanupEvents, bool fails) : IScenarioLifecycleExpo
    {
        private bool _absent;

        public ExpoWebIdentity Identity { get; } = ExpoWebIdentity.Create();

        public ValueTask DisposeAsync()
        {
            cleanupEvents.Add("expo");
            _absent = true;
            return fails
                ? ValueTask.FromException(new IOException("private lifecycle canary"))
                : ValueTask.CompletedTask;
        }

        public Task<bool> ConfirmAbsentAsync() => Task.FromResult(_absent);
    }

    private sealed class RecordingBrowserRun(ICollection<string> cleanupEvents, bool fails) : IScenarioLifecycleBrowserRun
    {
        public ValueTask DisposeAsync()
        {
            cleanupEvents.Add("browser-run");
            return fails
                ? ValueTask.FromException(new IOException("private lifecycle canary"))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingBrowserScenario(ICollection<string> cleanupEvents, bool fails) : IScenarioLifecycleBrowserScenario
    {
        public IPage Page => null!;

        public ValueTask DisposeAsync()
        {
            cleanupEvents.Add("browser-scenario");
            return fails
                ? ValueTask.FromException(new IOException("private lifecycle canary"))
                : ValueTask.CompletedTask;
        }

        public Task<bool> ConfirmStorageIsEmptyAsync() => Task.FromResult(true);
    }

    private sealed class ScenarioLifecycleFixture(string root) : IAsyncDisposable
    {
        internal static Task<ScenarioLifecycleFixture> CreateAsync() => Task.FromResult(
            new ScenarioLifecycleFixture(Directory.CreateTempSubdirectory("lgym-e2e-scenario-lifecycle-").FullName));

        internal ScenarioLifecycleRequest CreateRequest(
            string caseId,
            ScenarioLifecycleObservation? previous = null,
            IRunDirectoryCleaner? cleaner = null)
        {
            var options = new E2EOptions
            {
                Runtime = new E2ERuntimeOptions { PrivateRunRoot = ".e2e-private/runs" },
                Timeouts = new E2ETimeoutsOptions
                {
                    ProcessShutdownSeconds = 2,
                    ScenarioSeconds = 5,
                    BrowserActionMilliseconds = 1_000
                }
            };
            var run = LifecycleRunDirectoryLease.Create(
                new PrivateRunDirectoryRequest(
                    root,
                    options.Runtime.PrivateRunRoot,
                    TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds)),
                cleaner);
            return new ScenarioLifecycleRequest(run, null, options, null!, root, caseId, previous);
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

    private sealed class FailingRunDirectoryCleaner : IRunDirectoryCleaner
    {
        public Task DeleteAsync(string runDirectory, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("private lifecycle path cleanup canary"));
    }
}
