using System.Net;
using System.Text.RegularExpressions;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class DatabaseBackedApiReadinessProbeTests
{
    private static readonly BoundaryRule[] ForbiddenRules =
    [
        new("product namespace", @"\bLgymApi\.(?:Api|Application|Domain|Infrastructure|Identity|Platform|TrainingPlanning|Notifications|BackgroundWorker(?:\.Common)?|Resources(?:\.Generator)?)(?:\.|\b)"),
        new("Entity Framework", @"(?:using\s+|global::)?Microsoft\.EntityFrameworkCore|\bDbContext\b"),
        new("Npgsql", @"(?:using\s+|global::)?Npgsql|\bNpgsqlConnection\b"),
        new("repository", @"\b\w*Repository\b"),
        new("in-process host", @"\bWebApplicationFactory\b"),
        new("container persistence", @"\bTestcontainers\b"),
        new("SQL", @"\b(SELECT|INSERT|UPDATE|DELETE|ALTER|DROP|CREATE)\s+", RegexOptions.IgnoreCase),
        new("direct SQL API", @"\b(?:ExecuteSql(?:Raw|Interpolated)?|FromSql(?:Raw|Interpolated)?|SqlQueryRaw)\s*\("),
        new("test endpoint", @"(?:api/(?:internal|test)|proof/|test-only)", RegexOptions.IgnoreCase)
    ];

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
    public async Task Testing_host_preserves_health_only_readiness_without_a_migrated_database()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var databaseProbe = new ScriptedDatabaseBackedApiReadinessProbe(
            DatabaseBackedApiReadinessOutcome.UnexpectedStatus);
        var host = await ExternalApiHostLease.StartAsync(
            fixture.CreateRequest(new FakeApiHostDatabaseLease(cleanupOrder)) with
            {
                EnvironmentName = "Testing"
            },
            new ExternalApiHostInfrastructure(
                new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder),
                new FakeExternalApiProcessStarter([null], cleanupOrder),
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([47111]),
                databaseProbe));

        await host.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(databaseProbe.CallCount, Is.Zero);
            Assert.That(cleanupOrder, Is.EqualTo(["api-process", "runtime-configuration", "postgresql"]));
            Assert.That(host.CleanupReceipt.AllResourcesAbsent, Is.True);
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
        var configurationOwnedByApi =
            string.Equals(Path.GetDirectoryName(runtime.ConfigurationPath), api.ComponentDirectory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(runtime.ConfigurationPath), "appsettings.e2e.json", StringComparison.Ordinal);
        var tempOwnedByApi =
            string.Equals(Path.GetDirectoryName(tempDirectory), api.ComponentDirectory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(tempDirectory), "temp", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(configurationOwnedByApi, Is.True);
            Assert.That(tempOwnedByApi, Is.True);
            Assert.That(Directory.Exists(api.ComponentDirectory), Is.False);
            Assert.That(Directory.Exists(sibling.ComponentDirectory), Is.True);
            Assert.That(Directory.Exists(run.RunDirectory), Is.True);
        });

        await scenario.DisposeAsync();
    }

    [Test]
    public async Task DatabaseBacked_api_runtime_rejects_an_ancestor_reparse_before_any_foreign_write()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var foreignDirectory = Path.Combine(repositoryRoot, ".e2e-private", "task-3-api-ancestor-foreign");
        var foreignMarker = Path.Combine(foreignDirectory, "foreign.marker");
        Directory.CreateDirectory(foreignDirectory);
        File.WriteAllText(foreignMarker, "foreign");
        await using var run = LifecycleRunDirectoryLease.Create(
            new PrivateRunDirectoryRequest(repositoryRoot, ".e2e-private/runs", TimeSpan.FromSeconds(1)));
        var scenario = run.CreateScenario("api-ancestor");
        var api = scenario.CreateApiComponent();
        var scenariosDirectory = Path.Combine(run.RunDirectory, "scenarios");
        Directory.Delete(scenariosDirectory, recursive: true);
        Directory.CreateSymbolicLink(scenariosDirectory, foreignDirectory);
        var request = new RuntimeConfigurationRequest(
            new PrivateRunDirectoryRequest(repositoryRoot, ".e2e-private/runs", TimeSpan.FromSeconds(1)),
            new ApiRuntimeDatabase("synthetic-connection"),
            ApiRuntimeConfigurationProfile.E2E);

        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await RuntimeConfigurationLease.CreateAsync(request, api));
            var foreignMarkerPresent = File.Exists(foreignMarker);
            var foreignConfigurationPresent = File.Exists(Path.Combine(
                foreignDirectory,
                "api-ancestor",
                "api",
                "appsettings.e2e.json"));

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
                Assert.That(foreignMarkerPresent, Is.True);
                Assert.That(foreignConfigurationPresent, Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(scenariosDirectory))
            {
                Directory.Delete(scenariosDirectory);
            }

            if (Directory.Exists(foreignDirectory))
            {
                Directory.Delete(foreignDirectory, recursive: true);
            }

            await scenario.DisposeAsync();
        }
    }

    [Test]
    public async Task DatabaseBacked_api_runtime_revalidates_ancestors_during_the_atomic_write()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var foreignDirectory = Path.Combine(repositoryRoot, ".e2e-private", "task-3-api-write-race-foreign");
        var foreignMarker = Path.Combine(foreignDirectory, "foreign.marker");
        Directory.CreateDirectory(foreignDirectory);
        File.WriteAllText(foreignMarker, "foreign");
        await using var run = LifecycleRunDirectoryLease.Create(
            new PrivateRunDirectoryRequest(repositoryRoot, ".e2e-private/runs", TimeSpan.FromSeconds(1)));
        var scenario = run.CreateScenario("api-write-race");
        var api = scenario.CreateApiComponent();
        var writer = new AncestorReparseFileWriter(foreignDirectory);
        var request = new RuntimeConfigurationRequest(
            new PrivateRunDirectoryRequest(repositoryRoot, ".e2e-private/runs", TimeSpan.FromSeconds(1)),
            new ApiRuntimeDatabase("synthetic-connection"),
            ApiRuntimeConfigurationProfile.E2E);

        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await RuntimeConfigurationLease.CreateAsync(
                    request,
                    api,
                    new RuntimeConfigurationInfrastructure(writer, new FileSystemRunDirectoryCleaner())));
            var foreignMarkerPresent = File.Exists(foreignMarker);
            var foreignConfigurationPresent = File.Exists(writer.ForeignConfigurationPath);

            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Is.EqualTo(PrivateRunDirectoryLease.PathValidationMessage));
                Assert.That(foreignMarkerPresent, Is.True);
                Assert.That(foreignConfigurationPresent, Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(foreignDirectory))
            {
                Directory.Delete(foreignDirectory, recursive: true);
            }

            await scenario.DisposeAsync();
        }
    }

    [Test]
    public void DatabaseBacked_readiness_source_has_only_the_public_HTTP_boundary()
    {
        var lifecycleDirectory = Path.Combine(
            RepositoryRoot.Find(),
            "LgymApi.E2ETests",
            "Lifecycle");
        var violations = FindLifecycleBoundaryViolations(lifecycleDirectory).ToArray();

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [TestCase("using LgymApi.Domain;")]
    [TestCase("global::LgymApi.Infrastructure")]
    [TestCase("LgymApi.Domain.Account")]
    [TestCase("LgymApi.Resources.Messages")]
    [TestCase("LgymApi.Resources.Generator.ResourceGenerator")]
    [TestCase("using Microsoft.EntityFrameworkCore;")]
    [TestCase("global::Microsoft.EntityFrameworkCore.DbContext")]
    [TestCase("using Npgsql;")]
    [TestCase("AccountRepository")]
    [TestCase("WebApplicationFactory")]
    [TestCase("Testcontainers")]
    [TestCase("SELECT * FROM users")]
    [TestCase("database.ExecuteSqlRaw(unsafeCommand)")]
    [TestCase("api/internal/example")]
    [TestCase("api/test/example")]
    [TestCase("proof/example")]
    [TestCase("test-only")]
    public void DatabaseBacked_readiness_source_policy_rejects_each_forbidden_boundary(string unsafeFixture)
    {
        var violations = FindBoundaryViolations("unsafe.cs", unsafeFixture).ToArray();

        Assert.That(violations, Is.Not.Empty);
    }

    [Test]
    public void DatabaseBacked_readiness_source_policy_scans_nested_non_test_files()
    {
        var lifecycleDirectory = Path.Combine(
            RepositoryRoot.Find(),
            "LgymApi.E2ETests",
            "Lifecycle");
        var fixtureDirectory = Path.Combine(lifecycleDirectory, "task-3-policy-fixture");
        var fixturePath = Path.Combine(fixtureDirectory, "NestedBoundaryBypass.cs");
        Directory.CreateDirectory(fixtureDirectory);
        File.WriteAllText(fixturePath, "LgymApi.Resources.Messages");

        try
        {
            var violations = FindLifecycleBoundaryViolations(lifecycleDirectory).ToArray();

            Assert.That(violations, Is.Not.Empty);
        }
        finally
        {
            if (Directory.Exists(fixtureDirectory))
            {
                Directory.Delete(fixtureDirectory, recursive: true);
            }
        }
    }

    private static IEnumerable<string> FindLifecycleBoundaryViolations(string lifecycleDirectory) =>
        Directory.EnumerateFiles(lifecycleDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("Tests.cs", StringComparison.Ordinal))
            .SelectMany(path => FindBoundaryViolations(path, File.ReadAllText(path)));

    private static IEnumerable<string> FindBoundaryViolations(string path, string source) =>
        ForbiddenRules
            .Where(rule => rule.Pattern.IsMatch(source))
            .Select(rule => $"{Path.GetFileName(path)} violates public-HTTP boundary rule '{rule.Name}'.");

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

    private sealed class AncestorReparseFileWriter(string foreignDirectory) : IRuntimeConfigurationFileWriter
    {
        private readonly AtomicRuntimeConfigurationFileWriter _writer = new();

        internal string ForeignConfigurationPath => Path.Combine(
            foreignDirectory,
            "api-write-race",
            "api",
            "appsettings.e2e.json");

        public async Task WriteAsync(
            RuntimeConfigurationFileWriteRequest request,
            CancellationToken cancellationToken)
        {
            var apiDirectory = Path.GetDirectoryName(request.Path)!;
            var scenarioDirectory = Path.GetDirectoryName(apiDirectory)!;
            var scenariosDirectory = Path.GetDirectoryName(scenarioDirectory)!;
            Directory.Delete(scenariosDirectory, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(ForeignConfigurationPath)!);
            Directory.CreateSymbolicLink(scenariosDirectory, foreignDirectory);
            try
            {
                await _writer.WriteAsync(request, cancellationToken);
            }
            finally
            {
                if (Directory.Exists(scenariosDirectory))
                {
                    Directory.Delete(scenariosDirectory);
                }

                Directory.CreateDirectory(scenariosDirectory);
            }
        }
    }

    private sealed record BoundaryRule
    {
        internal BoundaryRule(string name, string pattern, RegexOptions options = RegexOptions.None)
        {
            Name = name;
            Pattern = new Regex(pattern, RegexOptions.CultureInvariant | options);
        }

        internal string Name { get; }

        internal Regex Pattern { get; }
    }
}
