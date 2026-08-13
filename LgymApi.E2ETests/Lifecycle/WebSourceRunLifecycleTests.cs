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
            Assert.That(firstRequest.EnvironmentVariables["HOME"], Is.Not.EqualTo(secondRequest.EnvironmentVariables["HOME"]));
            Assert.That(firstRequest.EnvironmentVariables["APPDATA"], Is.Not.EqualTo(secondRequest.EnvironmentVariables["APPDATA"]));
            Assert.That(firstRequest.EnvironmentVariables["TEMP"], Is.Not.EqualTo(secondRequest.EnvironmentVariables["TEMP"]));
            Assert.That(firstRequest.EnvironmentVariables["npm_config_cache"], Is.EqualTo(secondRequest.EnvironmentVariables["npm_config_cache"]));
            Assert.That(PrivateRunDirectoryLayout.IsDescendantOrSame(firstRuntime.ComponentDirectory, firstRequest.EnvironmentVariables["HOME"]!), Is.True);
            Assert.That(PrivateRunDirectoryLayout.IsDescendantOrSame(secondRuntime.ComponentDirectory, secondRequest.EnvironmentVariables["HOME"]!), Is.True);
            Assert.That(first.Identity, Is.Not.EqualTo(second.Identity));
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

    private static WebSourceRunDependencies CreateDependencies(
        Task3WebSourceRunFixture fixture,
        Task3NodeNpmCommandRunner npm) =>
        new()
        {
            Stager = new Task3WebSourceStager(),
            ToolResolver = fixture.CreateToolResolver(),
            CommandRunner = npm
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
        public OwnedExternalProcessCleanupReceipt CleanupReceipt { get; } = new(
            new ExternalProcessOutput(string.Empty, false),
            new ExternalProcessOutput(string.Empty, false),
            new ProcessCleanupReceipt([], true),
            true,
            true);
        OwnedExternalProcessCleanupReceipt? IExpoWebProcess.CleanupReceipt => CleanupReceipt;
        internal int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
