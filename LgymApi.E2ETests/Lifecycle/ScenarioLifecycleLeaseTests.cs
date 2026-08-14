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

    [TestCase("browser-scenario")]
    [TestCase("browser-run")]
    [TestCase("expo")]
    [TestCase("external-api-host")]
    public async Task ScenarioLifecycleLease_timeout_retains_the_child_before_any_parent_cleanup(string blockedCategory)
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        var dependencies = new RecordingScenarioLifecycleDependencies(blockedCleanup: blockedCategory);
        var lease = await ScenarioLifecycleLease.CreateAsync(
            fixture.CreateRequest("scenario-lifecycle-retained-timeout", shutdownSeconds: 1),
            dependencies);

        try
        {
            var exception = Assert.ThrowsAsync<ScenarioLifecycleCleanupException>(async () => await lease.DisposeAsync());
            await dependencies.BlockingCleanup!.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Receipt.CleanupFailureCount, Is.EqualTo(1));
                Assert.That(dependencies.BlockingCleanup.DisposeCount, Is.EqualTo(1));
                Assert.That(dependencies.CleanupEvents, Is.EqualTo(CleanupThrough(blockedCategory)));
            });
        }
        finally
        {
            dependencies.BlockingCleanup!.Release();
            await dependencies.BlockingCleanup.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => dependencies.CleanupEvents.Count == 5);
            await WaitUntilAsync(() => fixture.ScenarioWasRemoved("scenario-lifecycle-retained-timeout"));
            Assert.ThrowsAsync<ScenarioLifecycleCleanupException>(async () => await lease.DisposeAsync());
            Assert.That(dependencies.BlockingCleanup.DisposeCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ScenarioLifecycleLease_retained_cleanup_must_complete_before_its_observation_is_reused()
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        var firstDependencies = new RecordingScenarioLifecycleDependencies(blockedCleanup: "external-api-host");
        var first = await ScenarioLifecycleLease.CreateAsync(
            fixture.CreateRequest("scenario-lifecycle-retained-observation", shutdownSeconds: 1),
            firstDependencies);

        try
        {
            Assert.ThrowsAsync<ScenarioLifecycleCleanupException>(async () => await first.DisposeAsync());
            await firstDependencies.BlockingCleanup!.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var retainedCleanup = first.WaitForRetainedCleanupAsync(CancellationToken.None);
            Assert.That(retainedCleanup.IsCompleted, Is.False);

            firstDependencies.BlockingCleanup.Release();
            await retainedCleanup.WaitAsync(TimeSpan.FromSeconds(2));

            var secondDependencies = new RecordingScenarioLifecycleDependencies();
            var second = await ScenarioLifecycleLease.CreateAsync(
                fixture.CreateRequest("scenario-lifecycle-after-retained", first.Observation),
                secondDependencies);
            await second.DisposeAsync();

            Assert.That(second.Receipt.PreviousResourcesAbsent, Is.True);
        }
        finally
        {
            firstDependencies.BlockingCleanup!.Release();
        }
    }

    [Test]
    public async Task ScenarioLifecycleLease_preserves_the_primary_startup_failure_while_browser_cleanup_is_retained()
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        var dependencies = new RecordingScenarioLifecycleDependencies(
            ScenarioLifecycleFailureStage.BrowserPage,
            blockedCleanup: "browser-run");

        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await ScenarioLifecycleLease.CreateAsync(
                    fixture.CreateRequest("scenario-lifecycle-primary-retained", shutdownSeconds: 1),
                    dependencies));
            var receipt = (ScenarioLifecycleReceipt)exception!.Data[nameof(ScenarioLifecycleReceipt)]!;
            await dependencies.BlockingCleanup!.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Is.EqualTo("private lifecycle canary"));
                Assert.That(receipt.CleanupFailureCount, Is.EqualTo(1));
                Assert.That(dependencies.CleanupEvents, Is.EqualTo(["browser-run"]));
            });
        }
        finally
        {
            dependencies.BlockingCleanup!.Release();
            await dependencies.BlockingCleanup.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => dependencies.CleanupEvents.Count == 4);
            await WaitUntilAsync(() => fixture.ScenarioWasRemoved("scenario-lifecycle-primary-retained"));
        }
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
    public async Task Scenario_failure_after_the_ready_stack_preserves_the_primary_failure_writes_one_safe_artifact_and_starts_fresh()
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        const string failedCaseId = "scenario-failure-artifact";
        var request = fixture.CreateRequest(failedCaseId);
        var dependencies = new RecordingScenarioLifecycleDependencies();
        ScenarioLifecycleObservation? firstObservation = null;

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ScenarioLifecycleLease.ExecuteAsync(
                request,
                (lease, _) =>
                {
                    firstObservation = lease.Observation;
                    Assert.That(lease.Receipt.AcquiredCategories, Is.EqualTo([
                        "scenario-paths", "postgresql", "external-api-host", "expo", "browser-run", "browser-scenario"]));
                    throw new InvalidOperationException("scenario callback canary");
                },
                CreateFailureArtifactWriter(),
                dependencies));
        var receipt = (ScenarioLifecycleReceipt)exception!.Data[nameof(ScenarioLifecycleReceipt)]!;
        var artifactDirectory = Path.Combine(request.Run.RunDirectory, "artifacts", failedCaseId);
        var artifactPath = Path.Combine(artifactDirectory, ScenarioFailureArtifactWriter.FileName);
        var artifact = await File.ReadAllTextAsync(artifactPath);

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("scenario callback canary"));
            Assert.That(receipt.CleanupFailureCount, Is.Zero);
            Assert.That(receipt.DatabaseAbsent && receipt.ApiAbsent && receipt.ExpoAbsent && receipt.ScenarioPathsAbsent, Is.True);
            Assert.That(artifact, Does.Contain("\"failureCategory\":\"scenario-callback-failure\""));
            Assert.That(artifact, Does.Not.Contain("scenario callback canary"));
            Assert.That(artifact, Does.Not.Contain("safe-test-connection"));
            Assert.That(artifact, Does.Not.Contain("ProcessId"));
            Assert.That(artifact, Does.Not.Contain("storageState"));
        });

        File.Delete(artifactPath);
        Directory.Delete(artifactDirectory);
        Assert.That(Directory.Exists(artifactDirectory), Is.False);

        var freshDependencies = new RecordingScenarioLifecycleDependencies();
        var second = await ScenarioLifecycleLease.CreateAsync(
            fixture.CreateRequest("scenario-failure-fresh", firstObservation, run: request.Run),
            freshDependencies);
        await second.DisposeAsync();
        await request.Run.FinalizeFailureAsync();

        Assert.Multiple(() =>
        {
            Assert.That(second.Receipt.PreviousResourcesAbsent, Is.True);
            Assert.That(second.Receipt.DatabaseIdentityDistinct, Is.True);
            Assert.That(Directory.Exists(Path.Combine(request.Run.RunDirectory, "scenarios")), Is.False);
        });
    }

    [Test]
    public async Task Scenario_failure_continues_cleanup_when_a_resource_and_diagnostics_write_fail()
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        var request = fixture.CreateRequest("scenario-diagnostics-write-failure");
        var dependencies = new RecordingScenarioLifecycleDependencies(cleanupFailure: "browser-run");
        var writer = new ScenarioFailureArtifactWriter(CreatePublicationReceipt(), new FailingArtifactFileSystem());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ScenarioLifecycleLease.ExecuteAsync(
                request,
                (_, _) => throw new InvalidOperationException("primary callback canary"),
                writer,
                dependencies));
        var receipt = (ScenarioLifecycleReceipt)exception!.Data[nameof(ScenarioLifecycleReceipt)]!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.Message, Is.EqualTo("primary callback canary"));
            Assert.That(receipt.CleanupFailureCount, Is.EqualTo(2));
            Assert.That(receipt.AttemptedCleanupCategories, Is.EqualTo([
                "browser-scenario", "browser-run", "expo", "external-api-host", "scenario-paths"]));
            Assert.That(dependencies.CleanupEvents, Is.EqualTo([
                "browser-scenario", "browser-run", "expo", "external-api-host", "postgresql"]));
            Assert.That(receipt.DatabaseAbsent && receipt.ApiAbsent && receipt.ExpoAbsent && receipt.ScenarioPathsAbsent, Is.True);
            Assert.That(File.Exists(Path.Combine(request.Run.RunDirectory, "artifacts", "scenario-diagnostics-write-failure", ScenarioFailureArtifactWriter.FileName)), Is.False);
        });

        await request.Run.FinalizeFailureAsync();
    }

    [Test]
    public async Task Scenario_success_writes_no_failure_artifact_and_removes_the_completed_run()
    {
        await using var fixture = await ScenarioLifecycleFixture.CreateAsync();
        const string caseId = "scenario-success-no-artifact";
        var request = fixture.CreateRequest(caseId);

        var receipt = await ScenarioLifecycleLease.ExecuteAsync(
            request,
            (_, _) => Task.CompletedTask,
            CreateFailureArtifactWriter(),
            new RecordingScenarioLifecycleDependencies());
        await request.Run.FinalizeSuccessAsync();

        Assert.Multiple(() =>
        {
            Assert.That(receipt.CleanupFailureCount, Is.Zero);
            Assert.That(Directory.Exists(Path.Combine(request.Run.RunDirectory, "artifacts", caseId)), Is.False);
            Assert.That(Directory.Exists(request.Run.RunDirectory), Is.False);
        });
    }

    [Test]
    public async Task Scenario_proof_provisions_once_under_the_run_lifetime_before_the_scenario_lifetime_starts()
    {
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var request = fixture.CreateRequest();
        request.Options.Timeouts.TestSessionSeconds = 5;
        request.Options.Timeouts.ScenarioSeconds = 30;
        using var lifetime = ScenarioLifecycleProofLifetime.Create(request.Options);
        await using var run = LifecycleRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            fixture.OwnerRoot,
            ".e2e-private/runs",
            TimeSpan.FromSeconds(2)));
        var stager = new RecordingProvisioningStager();
        var npm = new BlockingProvisioningCommandRunner();
        await using var source = await WebSourceRunLease.CreateAsync(
            request,
            new WebSourceRunDependencies
            {
                Stager = stager,
                ToolResolver = fixture.CreateToolResolver(),
                CommandRunner = npm
            },
            run,
            lifetime.ProvisioningToken);
        var installation = source.EnsureInstalledAsync(lifetime.ProvisioningToken);
        var repeated = source.EnsureInstalledAsync(lifetime.ProvisioningToken);

        try
        {
            await npm.NpmStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            using var scenario = lifetime.StartScenario();
            scenario.Cancel();
            var completed = await Task.WhenAny(installation, Task.Delay(200));
            var installToken = npm.ObservedInstallToken!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(repeated, Is.SameAs(installation));
                Assert.That(completed, Is.Not.SameAs(installation));
                Assert.That(lifetime.ProvisioningToken.IsCancellationRequested, Is.False);
                Assert.That(stager.ObservedToken, Is.EqualTo(lifetime.ProvisioningToken));
                Assert.That(installToken.IsCancellationRequested, Is.False);
                Assert.That(installToken, Is.Not.EqualTo(scenario.Token));
                Assert.That(scenario.Token, Is.Not.EqualTo(lifetime.ProvisioningToken));
                Assert.That(npm.NpmInvocationCount, Is.EqualTo(1));
            });
        }
        finally
        {
            npm.Release();
            try
            {
                await installation;
            }
            catch (OperationCanceledException)
            {
            }
        }

        Assert.That(source.IsInstalled, Is.True);
    }

    [Test]
    public async Task ScenarioLifecycleLease_real_stack_returns_an_accessible_Expo_page_after_both_API_gates()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, repositoryRoot);
        using var lifetime = ScenarioLifecycleProofLifetime.Create(options);
        var api = await RealApiHostProofContext.CreateAsync(lifetime.ProvisioningToken);
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
            lifetime.ProvisioningToken);
        await source.EnsureInstalledAsync(lifetime.ProvisioningToken);
        using var scenario = lifetime.StartScenario();
        await using var lease = await ScenarioLifecycleLease.CreateAsync(
            new ScenarioLifecycleRequest(
                run,
                source,
                api.Options,
                api.Publication,
                api.RepositoryRoot,
                "scenario-lifecycle-real"),
            cancellationToken: scenario.Token);

        var navigation = lease.Page.GotoAsync("/", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Commit,
            Timeout = api.Options.Timeouts.BrowserActionMilliseconds
        });
        var response = await navigation.WaitAsync(scenario.Token);

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
        string? cleanupFailure = null,
        string? blockedCleanup = null) : IScenarioLifecycleDependencies
    {
        internal List<string> AcquisitionEvents { get; } = [];
        internal List<string> CleanupEvents { get; } = [];
        internal RecordingDatabase Database { get; } = new();
        internal BlockingCleanupResource? BlockingCleanup { get; } = blockedCleanup is null ? null : new();

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

            return new RecordingApiHost(
                database,
                CleanupEvents,
                cleanupFailure == "external-api-host",
                blockedCleanup == "external-api-host" ? BlockingCleanup : null);
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

            return Task.FromResult<IScenarioLifecycleExpo>(new RecordingExpo(
                CleanupEvents,
                cleanupFailure == "expo",
                blockedCleanup == "expo" ? BlockingCleanup : null));
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
                cleanupFailure == "browser-run",
                blockedCleanup == "browser-run" ? BlockingCleanup : null));
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
                cleanupFailure == "browser-scenario",
                blockedCleanup == "browser-scenario" ? BlockingCleanup : null));
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
        bool fails,
        BlockingCleanupResource? blocker) : IScenarioLifecycleApiHost
    {
        public Uri BaseAddress { get; } = new("http://127.0.0.1:40123/");

        public ExternalApiHostObservation Observation { get; } = new(
            ScenarioResourceIdentity.Create(),
            () => new ExternalApiHostCleanupReceipt(true, true, true, [], 0));

        public async ValueTask DisposeAsync()
        {
            cleanupEvents.Add("external-api-host");
            if (blocker is not null)
            {
                await blocker.DisposeAsync();
            }

            await database.DisposeAsync();
            cleanupEvents.Add("postgresql");
            if (fails)
            {
                throw new IOException("private lifecycle canary");
            }
        }
    }

    private sealed class RecordingExpo(
        ICollection<string> cleanupEvents,
        bool fails,
        BlockingCleanupResource? blocker) : IScenarioLifecycleExpo
    {
        private bool _absent;

        public ExpoWebIdentity Identity { get; } = ExpoWebIdentity.Create();

        public ValueTask DisposeAsync()
        {
            cleanupEvents.Add("expo");
            _absent = true;
            if (blocker is not null)
            {
                return blocker.DisposeAsync();
            }

            return fails
                ? ValueTask.FromException(new IOException("private lifecycle canary"))
                : ValueTask.CompletedTask;
        }

        public Task<bool> ConfirmAbsentAsync() => Task.FromResult(_absent);
    }

    private sealed class RecordingBrowserRun(
        ICollection<string> cleanupEvents,
        bool fails,
        BlockingCleanupResource? blocker) : IScenarioLifecycleBrowserRun
    {
        public ValueTask DisposeAsync()
        {
            cleanupEvents.Add("browser-run");
            if (blocker is not null)
            {
                return blocker.DisposeAsync();
            }

            return fails
                ? ValueTask.FromException(new IOException("private lifecycle canary"))
                : ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingBrowserScenario(
        ICollection<string> cleanupEvents,
        bool fails,
        BlockingCleanupResource? blocker) : IScenarioLifecycleBrowserScenario
    {
        public IPage Page => null!;

        public ValueTask DisposeAsync()
        {
            cleanupEvents.Add("browser-scenario");
            if (blocker is not null)
            {
                return blocker.DisposeAsync();
            }

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
            IRunDirectoryCleaner? cleaner = null,
            int shutdownSeconds = 2,
            LifecycleRunDirectoryLease? run = null)
        {
            var options = new E2EOptions
            {
                Runtime = new E2ERuntimeOptions { PrivateRunRoot = ".e2e-private/runs" },
                Timeouts = new E2ETimeoutsOptions
                {
                    ProcessShutdownSeconds = shutdownSeconds,
                    ScenarioSeconds = 5,
                    BrowserActionMilliseconds = 1_000
                }
            };
            var lifecycleRun = run ?? LifecycleRunDirectoryLease.Create(
                new PrivateRunDirectoryRequest(
                    root,
                    options.Runtime.PrivateRunRoot,
                    TimeSpan.FromSeconds(options.Timeouts.ProcessShutdownSeconds)),
                cleaner);
            return new ScenarioLifecycleRequest(lifecycleRun, null, options, null!, root, caseId, previous);
        }

        internal bool ScenarioWasRemoved(string caseId) => !Directory
            .EnumerateDirectories(root, "scenarios", SearchOption.AllDirectories)
            .SelectMany(directory => Directory.EnumerateDirectories(directory, caseId))
            .Any();

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

    private static ScenarioFailureArtifactWriter CreateFailureArtifactWriter() =>
        new(CreatePublicationReceipt());

    private static ApiPublicationReceipt CreatePublicationReceipt() => new(
        "publish",
        new string('b', 64),
        DateTimeOffset.UnixEpoch,
        new string('a', 40),
        false,
        new ApiPublicationProcessReceipt(0, false, false));

    private sealed class FailingArtifactFileSystem : IScenarioFailureArtifactFileSystem
    {
        public Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("artifact write canary"));

        public void Move(string sourcePath, string destinationPath) => throw new NotSupportedException();

        public bool FileExists(string path) => false;

        public void DeleteFile(string path) => throw new NotSupportedException();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("The retained lifecycle cleanup did not drain.");
    }

    private static IReadOnlyList<string> CleanupThrough(string category)
    {
        var order = new[] { "browser-scenario", "browser-run", "expo", "external-api-host" };
        return order.Take(Array.IndexOf(order, category) + 1).ToArray();
    }

    private sealed class ScenarioLifecycleProofLifetime : IDisposable
    {
        private readonly CancellationTokenSource _provisioning;
        private readonly TimeSpan _scenarioTimeout;

        private ScenarioLifecycleProofLifetime(E2EOptions options)
        {
            _provisioning = new CancellationTokenSource(
                TimeSpan.FromSeconds(options.Timeouts.TestSessionSeconds));
            _scenarioTimeout = TimeSpan.FromSeconds(options.Timeouts.ScenarioSeconds);
        }

        internal CancellationToken ProvisioningToken => _provisioning.Token;

        internal static ScenarioLifecycleProofLifetime Create(E2EOptions options) => new(options);

        internal CancellationTokenSource StartScenario() => new(_scenarioTimeout);

        public void Dispose() => _provisioning.Dispose();
    }

    private sealed class RecordingProvisioningStager : IWebSourceStager
    {
        private readonly Task3WebSourceStager _inner = new();

        internal CancellationToken? ObservedToken { get; private set; }

        public Task<PinnedWebSourceStage> StageAsync(
            E2EOptions options,
            PrivateRunDirectoryLease runLease,
            CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            return _inner.StageAsync(options, runLease, cancellationToken);
        }

        public Task<PinnedWebSourceStage> StageForLifecycleAsync(
            E2EOptions options,
            PrivateRunDirectoryLease runLease,
            CancellationToken cancellationToken) =>
            StageAsync(options, runLease, cancellationToken);
    }

    private sealed class BlockingProvisioningCommandRunner : INodeNpmCommandRunner
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource NpmStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal CancellationToken? ObservedInstallToken { get; private set; }

        internal int NpmInvocationCount { get; private set; }

        public async Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken)
        {
            if (request.Arguments.SequenceEqual(["--version"]))
            {
                return Result();
            }

            ObservedInstallToken = cancellationToken;
            NpmInvocationCount++;
            NpmStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return Result();
        }

        internal void Release() => _release.TrySetResult();

        private static ExternalProcessResult Result() => new(
            0,
            new ExternalProcessOutput("v22.18.0\n", false),
            new ExternalProcessOutput(string.Empty, false));
    }

    private sealed class BlockingCleanupResource : IAsyncDisposable
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int DisposeCount { get; private set; }

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            Started.TrySetResult();
            try
            {
                await _release.Task;
            }
            finally
            {
                Completed.TrySetResult();
            }
        }

        internal void Release() => _release.TrySetResult();
    }
}
