using LgymApi.E2ETests.Harness;
using System.Net;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class DatabaseBackedApiReadinessProbeTests
{
    [Test]
    public async Task DatabaseBacked_readiness_runs_after_health_and_rejects_a_non_401_response_before_later_acquisition()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([null], cleanupOrder);
        var databaseProbe = new ScriptedDatabaseBackedApiReadinessProbe(
            DatabaseBackedApiReadinessOutcome.UnexpectedStatus);
        ExternalApiHostLease? host = null;

        try
        {
            host = await ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(database),
                new ExternalApiHostInfrastructure(
                    runtimeFactory,
                    processStarter,
                    new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                    new FakeLoopbackPortAllocator([47101]),
                    databaseProbe));
            Assert.Fail("Database-backed readiness unexpectedly permitted later acquisition.");
        }
        catch (ExternalApiHostStartupException exception)
        {
            Assert.That(exception.Message, Is.EqualTo(ExternalApiHostLease.StartupFailureMessage));
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync();
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(databaseProbe.CallCount, Is.EqualTo(1));
            Assert.That(databaseProbe.BaseAddresses.Single().AbsolutePath, Is.EqualTo("/"));
            Assert.That(cleanupOrder, Is.EqualTo(["api-process", "runtime-configuration", "postgresql"]));
        });
    }

    [Test]
    public async Task ApiHostObservation_exposes_only_safe_cleanup_facts_after_disposal()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var host = await ExternalApiHostLease.StartAsync(
            fixture.CreateRequest(new FakeApiHostDatabaseLease(cleanupOrder)),
            new ExternalApiHostInfrastructure(
                new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder),
                new FakeExternalApiProcessStarter([null], cleanupOrder),
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([47102]),
                new ScriptedDatabaseBackedApiReadinessProbe(DatabaseBackedApiReadinessOutcome.Ready)));

        await host.DisposeAsync();

        var receiptProperties = host.CleanupReceipt.GetType()
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        var observationFactory = typeof(ExternalApiHostLease).GetMethod(
            "CreateObservation",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.Multiple(() =>
        {
            Assert.That(host.CleanupReceipt.ProcessTreeAbsent, Is.True);
            Assert.That(host.CleanupReceipt.RuntimeDirectoryAbsent, Is.True);
            Assert.That(host.CleanupReceipt.DatabaseAbsent, Is.True);
            Assert.That(receiptProperties, Does.Contain("ProcessTreeAbsent"));
            Assert.That(receiptProperties, Does.Contain("RuntimeDirectoryAbsent"));
            Assert.That(receiptProperties, Does.Contain("DatabaseAbsent"));
            Assert.That(observationFactory, Is.Not.Null);
        });
    }

    [TestCase(HttpStatusCode.Unauthorized, "Ready")]
    [TestCase(HttpStatusCode.OK, "UnexpectedStatus")]
    [TestCase(HttpStatusCode.InternalServerError, "UnexpectedStatus")]
    public async Task DatabaseBacked_probe_accepts_only_the_public_invalid_login_401(
        HttpStatusCode statusCode,
        string expectedOutcome)
    {
        using var client = new HttpClient(new StatusCodeHandler(statusCode));
        var probe = new DatabaseBackedApiReadinessProbe(client);

        var outcome = await probe.WaitUntilReadyAsync(
            new Uri("http://127.0.0.1:47103/"),
            new ApiHostReadinessBounds(TimeSpan.FromSeconds(1), TimeSpan.Zero),
            CancellationToken.None);

        Assert.That(outcome.ToString(), Is.EqualTo(expectedOutcome));
    }

    [Test]
    public async Task DatabaseBacked_probe_sanitizes_malformed_transport_as_a_readiness_failure()
    {
        using var client = new HttpClient(new ThrowingHandler());
        var probe = new DatabaseBackedApiReadinessProbe(client);

        var outcome = await probe.WaitUntilReadyAsync(
            new Uri("http://127.0.0.1:47104/"),
            new ApiHostReadinessBounds(TimeSpan.FromSeconds(1), TimeSpan.Zero),
            CancellationToken.None);

        Assert.That(outcome, Is.EqualTo(DatabaseBackedApiReadinessOutcome.HttpFailure));
    }

    [Test]
    public async Task DatabaseBacked_api_runtime_configuration_consumes_only_the_scenario_api_child()
    {
        await using var run = LifecycleRunDirectoryLease.Create(
            new PrivateRunDirectoryRequest(RepositoryRoot.Find(), ".e2e-private/runs", TimeSpan.FromSeconds(1)));
        var scenario = run.CreateScenario("api-readiness");
        var api = scenario.CreateApiComponent();
        var sibling = scenario.CreateWebRuntimeComponent();
        var request = new RuntimeConfigurationRequest(
            new PrivateRunDirectoryRequest(RepositoryRoot.Find(), ".e2e-private/runs", TimeSpan.FromSeconds(1)),
            new ApiRuntimeDatabase("synthetic-connection"),
            ApiRuntimeConfigurationProfile.E2E);

        var runtime = await RuntimeConfigurationLease.CreateAsync(request, api);
        var tempDirectory = runtime.CreatePrivateTempDirectory();
        await runtime.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(runtime.ConfigurationPath, Is.EqualTo(Path.Combine(api.ComponentDirectory, "appsettings.e2e.json")));
            Assert.That(tempDirectory, Is.EqualTo(Path.Combine(api.ComponentDirectory, "temp")));
            Assert.That(Directory.Exists(api.ComponentDirectory), Is.False);
            Assert.That(Directory.Exists(sibling.ComponentDirectory), Is.True);
            Assert.That(Directory.Exists(run.RunDirectory), Is.True);
        });

        await scenario.DisposeAsync();
    }

    [Test]
    public void DatabaseBacked_readiness_source_has_only_the_public_HTTP_boundary()
    {
        var lifecycleDirectory = Path.Combine(
            RepositoryRoot.Find(),
            "LgymApi.E2ETests",
            "Lifecycle");
        var source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(lifecycleDirectory, "*.cs")
                .Where(path => !path.EndsWith("Tests.cs", StringComparison.Ordinal))
                .Select(File.ReadAllText));
        var forbidden = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "ExecuteSql",
            "Repository",
            "LgymApi.Api",
            "WebApplicationFactory",
            "test-only"
        };

        Assert.That(forbidden.Where(source.Contains), Is.Empty);
    }

    private sealed class ScriptedDatabaseBackedApiReadinessProbe(
        DatabaseBackedApiReadinessOutcome outcome) : IDatabaseBackedApiReadinessProbe
    {
        internal int CallCount { get; private set; }

        internal List<Uri> BaseAddresses { get; } = [];

        public Task<DatabaseBackedApiReadinessOutcome> WaitUntilReadyAsync(
            Uri baseAddress,
            ApiHostReadinessBounds bounds,
            CancellationToken cancellationToken)
        {
            CallCount++;
            BaseAddresses.Add(baseAddress);
            return Task.FromResult(outcome);
        }
    }

    private sealed class StatusCodeHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Multiple(() =>
            {
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/api/login"));
            });
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("synthetic transport failure"));
    }
}
