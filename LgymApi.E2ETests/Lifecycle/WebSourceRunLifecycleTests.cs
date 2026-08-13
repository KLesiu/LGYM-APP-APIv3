using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class WebSourceRunLifecycleTests
{
    [Test]
    public async Task WebSourceRun_reuses_one_install_but_gives_each_scenario_a_fresh_Expo_runtime_environment()
    {
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var priorSecret = Environment.GetEnvironmentVariable("LGYM_TASK4_SECRET");
        Environment.SetEnvironmentVariable("LGYM_TASK4_SECRET", "must-not-inherit");
        try
        {
            await using var run = LifecycleRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            fixture.OwnerRoot,
            ".e2e-private/runs",
            TimeSpan.FromSeconds(2)));
            var stager = new Task3WebSourceStager();
            var npm = new Task3NodeNpmCommandRunner();
            await using var source = await WebSourceRunLease.CreateAsync(
            fixture.CreateRequest(),
            new WebSourceRunDependencies
            {
                Stager = stager,
                ToolResolver = fixture.CreateToolResolver(),
                CommandRunner = npm
            },
            run);
            await Task.WhenAll(source.EnsureInstalledAsync(), source.EnsureInstalledAsync());

            var firstScenario = run.CreateScenario("scenario-expo-a");
            var firstRuntime = firstScenario.CreateWebRuntimeComponent();
            var starter = new RecordingExpoWebProcessStarter();
            var first = await ExpoWebLease.StartAsync(
            new ExpoWebStartRequest(source, new Uri("http://127.0.0.1:48123/"))
            {
                RuntimeDirectory = firstRuntime,
                Options = CreateOptions()
            },
            new ExpoWebDependencies(starter, new ScriptedExpoWebPortProbe(false),
                new ScriptedExpoWebReadinessMonitor([ExpoWebReadinessOutcome.Ready])));

            await first.DisposeAsync();
            await firstScenario.DisposeAsync();

            var secondScenario = run.CreateScenario("scenario-expo-b");
            var secondRuntime = secondScenario.CreateWebRuntimeComponent();
            var second = await ExpoWebLease.StartAsync(
            new ExpoWebStartRequest(source, new Uri("http://127.0.0.1:48124/"))
            {
                RuntimeDirectory = secondRuntime,
                Options = CreateOptions()
            },
            new ExpoWebDependencies(starter, new ScriptedExpoWebPortProbe(false),
                new ScriptedExpoWebReadinessMonitor([ExpoWebReadinessOutcome.Ready])));

            var firstRequest = starter.Requests[0];
            var secondRequest = starter.Requests[1];
            Assert.Multiple(() =>
        {
            Assert.That(npm.NpmInvocationCount, Is.EqualTo(1));
            Assert.That(stager.StageCount, Is.EqualTo(1));
            Assert.That(firstRequest.WorkingDirectory, Is.EqualTo(source.SourceDirectory));
            Assert.That(secondRequest.WorkingDirectory, Is.EqualTo(source.SourceDirectory));
            Assert.That(firstRequest.EnvironmentVariables["REACT_APP_BACKEND"], Is.EqualTo("http://127.0.0.1:48123/"));
            Assert.That(secondRequest.EnvironmentVariables["REACT_APP_BACKEND"], Is.EqualTo("http://127.0.0.1:48124/"));
            Assert.That(firstRequest.EnvironmentVariables["BROWSER"], Is.EqualTo("none"));
            Assert.That(firstRequest.EnvironmentVariables, Does.Not.ContainKey("LGYM_TASK4_SECRET"));
            Assert.That(secondRequest.EnvironmentVariables["BROWSER"], Is.EqualTo("none"));
            Assert.That(secondRequest.EnvironmentVariables, Does.Not.ContainKey("LGYM_TASK4_SECRET"));
            foreach (var variableName in new[] { "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "TEMP", "TMP" })
            {
                Assert.That(firstRequest.EnvironmentVariables[variableName], Is.Not.EqualTo(secondRequest.EnvironmentVariables[variableName]));
                Assert.That(PrivateRunDirectoryLayout.IsDescendantOrSame(firstRuntime.ComponentDirectory, firstRequest.EnvironmentVariables[variableName]!), Is.True);
                Assert.That(PrivateRunDirectoryLayout.IsDescendantOrSame(secondRuntime.ComponentDirectory, secondRequest.EnvironmentVariables[variableName]!), Is.True);
            }
            Assert.That(firstRequest.EnvironmentVariables["npm_config_cache"], Is.EqualTo(secondRequest.EnvironmentVariables["npm_config_cache"]));
            Assert.That(first.Identity, Is.Not.EqualTo(second.Identity));
            Assert.That(source.SourceReceipt.SourceStatePreserved, Is.True);
            Assert.That(source.SourceReceipt.PinnedCommitSha, Is.EqualTo("1111111111111111111111111111111111111111"));
        });

            await second.DisposeAsync();
            await secondScenario.DisposeAsync();
            await source.DisposeAsync();

            Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(run.RunDirectory), Is.True);
            Assert.That(Directory.Exists(source.SourceDirectory), Is.False);
            Assert.That(Directory.Exists(source.NpmCacheDirectory), Is.False);
            Assert.That(starter.Processes.All(process => process.DisposeCount == 1), Is.True);
            Assert.That(second.CleanupReceipt!.ProcessTreeAbsent, Is.True);
            Assert.That(second.CleanupReceipt.DrainsCompleted, Is.True);
            Assert.That(second.CleanupReceipt.InspectionCompleted, Is.True);
        });
        }
        finally
        {
            Environment.SetEnvironmentVariable("LGYM_TASK4_SECRET", priorSecret);
        }
    }

    [Test]
    public async Task Scenario_Expo_timeout_reaps_its_owned_process_before_a_fresh_scenario_starts()
    {
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        await using var run = LifecycleRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            fixture.OwnerRoot,
            ".e2e-private/runs",
            TimeSpan.FromSeconds(2)));
        await using var source = await WebSourceRunLease.CreateAsync(
            fixture.CreateRequest(),
            CreateDependencies(fixture, new Task3NodeNpmCommandRunner()),
            run);
        await source.EnsureInstalledAsync();
        var starter = new RecordingExpoWebProcessStarter();
        var timedOutScenario = run.CreateScenario("scenario-expo-timeout");
        var timedOutRuntime = timedOutScenario.CreateWebRuntimeComponent();

        var timeout = Assert.ThrowsAsync<ExpoWebStartupException>(async () => await ExpoWebLease.StartAsync(
            new ExpoWebStartRequest(source, new Uri("http://127.0.0.1:48123/"))
            {
                RuntimeDirectory = timedOutRuntime,
                Options = CreateOptions()
            },
            new ExpoWebDependencies(starter, new ScriptedExpoWebPortProbe(false),
                new ScriptedExpoWebReadinessMonitor([ExpoWebReadinessOutcome.StartupTimeout]))));
        Assert.Multiple(() =>
        {
            Assert.That(starter.Processes[0].DisposeCount, Is.EqualTo(1));
        });
        var firstCleanupReceipt = starter.Processes[0].CleanupReceipt;
        Assert.That(firstCleanupReceipt, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(firstCleanupReceipt!.Cleanup.AllAbsentOrReused, Is.True);
            Assert.That(firstCleanupReceipt.DrainCompleted, Is.True);
            Assert.That(firstCleanupReceipt.InspectionCompleted, Is.True);
        });
        await timedOutScenario.DisposeAsync();

        var nextScenario = run.CreateScenario("scenario-expo-fresh");
        var nextRuntime = nextScenario.CreateWebRuntimeComponent();
        var fresh = await ExpoWebLease.StartAsync(
            new ExpoWebStartRequest(source, new Uri("http://127.0.0.1:48124/"))
            {
                RuntimeDirectory = nextRuntime,
                Options = CreateOptions()
            },
            new ExpoWebDependencies(starter, new ScriptedExpoWebPortProbe(false),
                new ScriptedExpoWebReadinessMonitor([ExpoWebReadinessOutcome.Ready])));
        await fresh.DisposeAsync();
        await nextScenario.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(timeout!.Category, Is.EqualTo(ExpoWebStartupFailureCategory.Timeout));
            Assert.That(starter.Processes[0].DisposeCount, Is.EqualTo(1));
            Assert.That(starter.Processes[1].DisposeCount, Is.EqualTo(1));
            Assert.That(starter.Requests[0].EnvironmentVariables["HOME"], Is.Not.EqualTo(starter.Requests[1].EnvironmentVariables["HOME"]));
            Assert.That(fresh.CleanupReceipt!.ProcessTreeAbsent, Is.True);
        });
    }

    [Test]
    public async Task WebSourceRun_borrowed_cleanup_is_bounded_retryable_and_preserves_the_lifecycle_root()
    {
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        await using var run = LifecycleRunDirectoryLease.Create(new PrivateRunDirectoryRequest(
            fixture.OwnerRoot,
            ".e2e-private/runs",
            TimeSpan.FromMilliseconds(100)));
        var sourceCleaner = new BlockingSourceCleaner();
        var dependencies = CreateDependencies(fixture, new Task3NodeNpmCommandRunner(), sourceCleaner);
        var request = fixture.CreateRequest();
        request.Options.Timeouts.ProcessShutdownSeconds = 1;
        var source = await WebSourceRunLease.CreateAsync(request, dependencies, run);
        await source.EnsureInstalledAsync();

        var cleanup = source.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromMilliseconds(1500)));

        Assert.That(completed, Is.SameAs(cleanup));
        Assert.ThrowsAsync<WebSourceRunCleanupException>(async () => await cleanup);
        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(run.RunDirectory), Is.True);
            Assert.That(Directory.Exists(source.SourceDirectory), Is.True);
            Assert.That(sourceCleaner.ObservedCancellation, Is.True);
        });

        sourceCleaner.Release();
        await source.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(run.RunDirectory), Is.True);
            Assert.That(Directory.Exists(source.SourceDirectory), Is.False);
        });
    }

    private static WebSourceRunDependencies CreateDependencies(
        Task3WebSourceRunFixture fixture,
        Task3NodeNpmCommandRunner npm,
        IRunDirectoryCleaner? sourceCleaner = null) =>
        new()
        {
            Stager = new Task3WebSourceStager(),
            ToolResolver = fixture.CreateToolResolver(),
            CommandRunner = npm,
            SourceCleaner = sourceCleaner ?? new FileSystemRunDirectoryCleaner()
        };

    private static Configuration.E2EOptions CreateOptions() => new()
    {
        Web = new() { Port = 8083 },
        Timeouts = new()
        {
            WebStartupSeconds = 2,
            HttpRequestSeconds = 1,
            ProcessShutdownSeconds = 2,
            TestSessionSeconds = 9
        }
    };

    private sealed class RecordingExpoWebProcessStarter : IExpoWebProcessStarter
    {
        internal List<ExternalProcessRequest> Requests { get; } = [];
        internal List<RecordingExpoWebProcess> Processes { get; } = [];

        public IExpoWebProcess Start(ExternalProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var process = new RecordingExpoWebProcess();
            Processes.Add(process);
            return process;
        }
    }

    private sealed class RecordingExpoWebProcess : IExpoWebProcess
    {
        public Task<ExpoWebProcessExit> Exit { get; } = new TaskCompletionSource<ExpoWebProcessExit>().Task;
        public OwnedExternalProcessCleanupReceipt? CleanupReceipt { get; private set; }
        internal int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            CleanupReceipt = new OwnedExternalProcessCleanupReceipt(
                new ExternalProcessOutput(string.Empty, false),
                new ExternalProcessOutput(string.Empty, false),
                new ProcessCleanupReceipt([], true),
                true,
                true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingSourceCleaner : IRunDirectoryCleaner
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool ObservedCancellation { get; private set; }

        public async Task DeleteAsync(string runDirectory, CancellationToken cancellationToken)
        {
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObservedCancellation = true;
                throw;
            }

            await new FileSystemRunDirectoryCleaner().DeleteAsync(runDirectory, cancellationToken);
        }

        internal void Release() => _release.TrySetResult();
    }
}
