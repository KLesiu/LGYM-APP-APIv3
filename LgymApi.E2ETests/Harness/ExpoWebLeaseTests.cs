using System.Net;
using System.Net.Sockets;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class ExpoWebLeaseTests
{
    [Test]
    public async Task ExpoWeb_starts_exact_owned_node_npm_command_with_closed_environment_and_scenario_api_uri()
    {
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var source = await CreateInstalledSourceAsync(fixture);
        var processStarter = new FakeExpoWebProcessStarter();
        var lease = await ExpoWebLease.StartAsync(
            CreateStartRequest(source),
            new ExpoWebDependencies(processStarter, new ScriptedExpoWebPortProbe(false),
                new ScriptedExpoWebReadinessMonitor([ExpoWebReadinessOutcome.Ready])));

        try
        {
            var request = processStarter.Request!;
            Assert.Multiple(() =>
            {
                Assert.That(request.FileName, Is.EqualTo(fixture.NodeExecutable));
                Assert.That(request.Arguments, Is.EqualTo(new[] { fixture.NpmCliScript, "run", "web" }));
                Assert.That(request.WorkingDirectory, Is.EqualTo(source.SourceDirectory));
                Assert.That(request.ClearEnvironment, Is.True);
                Assert.That(request.EnvironmentVariables["REACT_APP_BACKEND"], Is.EqualTo("http://127.0.0.1:48123/"));
                Assert.That(request.EnvironmentVariables["EXPO_NO_TELEMETRY"], Is.EqualTo("1"));
                Assert.That(request.EnvironmentVariables["BROWSER"], Is.EqualTo("none"));
                Assert.That(request.EnvironmentVariables, Does.Not.ContainKey("EXPO_ROUTER_APP_ROOT"));
                Assert.That(request.EnvironmentVariables, Does.Not.ContainKey("EXPO_WEB_PARENT_CANARY"));
                Assert.That(request.ExecutionTimeout, Is.EqualTo(TimeSpan.FromSeconds(9)));
                Assert.That(lease.BaseUri, Is.EqualTo(new Uri("http://localhost:8083/")));
            });
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [TestCase("http-failure")]
    [TestCase("http-timeout")]
    [TestCase("startup-timeout")]
    [TestCase("process-exited")]
    public async Task ExpoWeb_maps_non_ready_transport_outcomes_and_reaps_owned_process(
        string outcomeName)
    {
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var source = await CreateInstalledSourceAsync(fixture);
        var processStarter = new FakeExpoWebProcessStarter();

        var exception = Assert.ThrowsAsync<ExpoWebStartupException>(async () => await ExpoWebLease.StartAsync(
            CreateStartRequest(source),
            new ExpoWebDependencies(processStarter, new ScriptedExpoWebPortProbe(false),
                new ScriptedExpoWebReadinessMonitor([ParseOutcome(outcomeName)]))));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExpoWebLease.StartupFailureMessage));
            Assert.That(processStarter.Process!.DisposeCount, Is.EqualTo(1));
            Assert.That(processStarter.Process.CleanupReceipt.Cleanup.AllAbsentOrReused, Is.True);
        });
        await source.DisposeAsync();
    }

    [Test]
    public async Task ExpoWeb_retains_sanitized_startup_category_when_owned_cleanup_fails()
    {
        // Given
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var source = await CreateInstalledSourceAsync(fixture);
        var processStarter = new FakeExpoWebProcessStarter { DisposeFailure = new IOException("cleanup canary") };

        try
        {
            // When
            var exception = Assert.ThrowsAsync<ExpoWebStartupException>(async () => await ExpoWebLease.StartAsync(
                CreateStartRequest(source),
                new ExpoWebDependencies(processStarter, new ScriptedExpoWebPortProbe(false),
                    new ScriptedExpoWebReadinessMonitor([ExpoWebReadinessOutcome.ProcessExited]))));

            // Then
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(ExpoWebLease.StartupFailureMessage));
                Assert.That(exception.Category, Is.EqualTo(ExpoWebStartupFailureCategory.ProcessExit));
                Assert.That(exception.CleanupFailed, Is.True);
                Assert.That(exception.ToString(), Does.Not.Contain("cleanup canary"));
            });
        }
        finally
        {
            await source.DisposeAsync();
        }
    }

    [Test]
    public async Task ExpoWeb_caller_cancellation_reaps_owned_process_before_source_disposal()
    {
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        var source = await CreateInstalledSourceAsync(fixture);
        var processStarter = new FakeExpoWebProcessStarter();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await ExpoWebLease.StartAsync(
            CreateStartRequest(source),
            new ExpoWebDependencies(processStarter, new ScriptedExpoWebPortProbe(false),
                new ScriptedExpoWebReadinessMonitor([ExpoWebReadinessOutcome.Ready])),
            cancellation.Token));

        Assert.That(processStarter.Process, Is.Null);
        await source.DisposeAsync();
    }

    [Test]
    public async Task Web_harness_rejects_occupied_port_without_terminating_foreign_process()
    {
        var listener = new TcpListener(IPAddress.Loopback, 8083);
        listener.Start();
        try
        {
            await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
            var source = await CreateInstalledSourceAsync(fixture);
            var processStarter = new FakeExpoWebProcessStarter();

            var exception = Assert.ThrowsAsync<ExpoWebStartupException>(async () => await ExpoWebLease.StartAsync(
                CreateStartRequest(source),
                new ExpoWebDependencies(processStarter, new LoopbackExpoWebPortProbe(),
                    new ScriptedExpoWebReadinessMonitor([ExpoWebReadinessOutcome.Ready]))));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(ExpoWebLease.PortOccupiedMessage));
                Assert.That(processStarter.Process, Is.Null);
                Assert.That(listener.Server.IsBound, Is.True);
            });
            await source.DisposeAsync();
        }
        finally
        {
            listener.Stop();
        }
    }

    [Test]
    public async Task ExpoWeb_readiness_monitor_retries_non_success_until_caller_cancellation()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new Uri($"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var processExit = new TaskCompletionSource<ExpoWebProcessExit>(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = RespondWithAsync(listener, "HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", cancellation.Token);

        Assert.That(async () => await new ExpoWebReadinessMonitor()
                .WaitUntilReadyAsync(endpoint, processExit.Task,
                    new ExpoWebReadinessBounds(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10)),
                    cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        await IgnoreCancellationAsync(server);
    }

    [Test]
    public async Task ExpoWeb_readiness_monitor_maps_owned_early_exit_before_transport_readiness()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var processExit = Task.FromResult(new ExpoWebProcessExit(23));

        var outcome = await new ExpoWebReadinessMonitor().WaitUntilReadyAsync(
            new Uri("http://localhost:1/"),
            processExit,
            new ExpoWebReadinessBounds(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10)),
            cancellation.Token);

        Assert.That(outcome, Is.EqualTo(ExpoWebReadinessOutcome.ProcessExited));
    }

    private static async Task<WebSourceRunLease> CreateInstalledSourceAsync(Task3WebSourceRunFixture fixture)
    {
        var source = await WebSourceRunLease.CreateAsync(
            fixture.CreateRequest(),
            new WebSourceRunDependencies
            {
                Stager = new Task3WebSourceStager(),
                ToolResolver = fixture.CreateToolResolver(),
                CommandRunner = new Task3NodeNpmCommandRunner()
            });
        await source.EnsureInstalledAsync();
        return source;
    }

    private static ExpoWebStartRequest CreateStartRequest(WebSourceRunLease source) =>
        new(source, new Uri("http://127.0.0.1:48123"))
        {
            Options = new()
            {
                Web = new() { Port = 8083 },
                Timeouts = new()
                {
                    WebStartupSeconds = 2,
                    HttpRequestSeconds = 1,
                    ProcessShutdownSeconds = 2,
                    TestSessionSeconds = 9
                }
            }
        };

    private static ExpoWebReadinessOutcome ParseOutcome(string outcomeName) => outcomeName switch
    {
        "http-failure" => ExpoWebReadinessOutcome.HttpFailure,
        "http-timeout" => ExpoWebReadinessOutcome.HttpTimeout,
        "startup-timeout" => ExpoWebReadinessOutcome.StartupTimeout,
        "process-exited" => ExpoWebReadinessOutcome.ProcessExited,
        _ => throw new ArgumentOutOfRangeException(nameof(outcomeName))
    };

    private static async Task RespondWithAsync(TcpListener listener, string response, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = client.GetStream();
                await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(response), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}

internal sealed class FakeExpoWebProcessStarter : IExpoWebProcessStarter
{
    internal FakeExpoWebProcess? Process { get; private set; }

    internal ExternalProcessRequest? Request { get; private set; }

    internal Exception? DisposeFailure { get; init; }

    public IExpoWebProcess Start(ExternalProcessRequest request, CancellationToken cancellationToken)
    {
        Request = request;
        Process = new FakeExpoWebProcess(DisposeFailure);
        return Process;
    }
}

internal sealed class FakeExpoWebProcess(Exception? disposeFailure = null) : IExpoWebProcess
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
        if (disposeFailure is not null)
        {
            throw disposeFailure;
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class ScriptedExpoWebPortProbe(bool occupied) : IExpoWebPortProbe
{
    public bool IsOccupied(int port) => occupied;
}

internal sealed class ScriptedExpoWebReadinessMonitor(IReadOnlyList<ExpoWebReadinessOutcome> outcomes)
    : IExpoWebReadinessMonitor
{
    private readonly Queue<ExpoWebReadinessOutcome> _outcomes = new(outcomes);

    public Task<ExpoWebReadinessOutcome> WaitUntilReadyAsync(
        Uri endpoint,
        Task<ExpoWebProcessExit> processExit,
        ExpoWebReadinessBounds bounds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_outcomes.Dequeue());
    }
}
