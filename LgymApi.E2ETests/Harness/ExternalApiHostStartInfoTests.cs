namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ExternalApiHostStartInfoTests
{
    [Test]
    public async Task ExternalApiHost_uses_canonical_DLL_isolated_environment_and_public_health_endpoint()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([null], cleanupOrder);
        var readiness = new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]);
        var portAllocator = new FakeLoopbackPortAllocator([43125]);
        var parentCanaryName = "LGYM_TASK5_PARENT_CANARY";
        var parentCanaryValue = "task-5-parent-secret-canary";
        var originalCanary = Environment.GetEnvironmentVariable(parentCanaryName);
        ExternalApiHostLease? lease = null;

        try
        {
            Environment.SetEnvironmentVariable(parentCanaryName, parentCanaryValue);
            lease = await ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(database),
                fixture.CreateInfrastructure(runtimeFactory, processStarter, readiness, portAllocator));
        }
        finally
        {
            Environment.SetEnvironmentVariable(parentCanaryName, originalCanary);
        }

        var request = processStarter.Requests.Single();
        var startInfo = processStarter.StartInfos.Single();
        var allowedEnvironment = new[]
        {
            "ASPNETCORE_ENVIRONMENT",
            "ASPNETCORE_URLS",
            "DOTNET_CLI_TELEMETRY_OPTOUT",
            "DOTNET_ENVIRONMENT",
            "DOTNET_NOLOGO",
            "LGYM_APP_CONFIG_PATH",
            "SystemRoot",
            "TEMP",
            "TMP",
            "WINDIR"
        };

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathFullyQualified(startInfo.FileName), Is.True);
            Assert.That(Path.GetFileName(startInfo.FileName), Is.EqualTo("dotnet.exe").IgnoreCase);
            Assert.That(startInfo.ArgumentList, Is.EqualTo(new[] { fixture.Publication.DllPath }));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(fixture.Publication.PublicationDirectory));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(request.ClearEnvironment, Is.True);
            Assert.That(startInfo.Environment.Keys.Order(), Is.EqualTo(allowedEnvironment.Order()));
            Assert.That(startInfo.Environment.ContainsKey("PATH"), Is.False);
            Assert.That(startInfo.Environment.ContainsKey(parentCanaryName), Is.False);
            Assert.That(startInfo.Environment.Values, Does.Not.Contain(parentCanaryValue));
            Assert.That(startInfo.Environment["ASPNETCORE_ENVIRONMENT"], Is.EqualTo("E2E"));
            Assert.That(startInfo.Environment["DOTNET_ENVIRONMENT"], Is.EqualTo("E2E"));
            Assert.That(startInfo.Environment["ASPNETCORE_URLS"], Is.EqualTo("http://127.0.0.1:43125"));
            Assert.That(startInfo.Environment["TEMP"], Is.EqualTo(runtimeFactory.Lease!.PrivateTempDirectory));
            Assert.That(startInfo.Environment["TMP"], Is.EqualTo(runtimeFactory.Lease.PrivateTempDirectory));
            Assert.That(startInfo.Environment["LGYM_APP_CONFIG_PATH"], Is.EqualTo(runtimeFactory.Lease.ConfigurationPath));
            Assert.That(readiness.HealthEndpoints.Single().AbsoluteUri, Is.EqualTo("http://127.0.0.1:43125/health/live"));
            Assert.That(lease!.BaseAddress.AbsoluteUri, Is.EqualTo("http://127.0.0.1:43125/"));
            Assert.That(runtimeFactory.Request!.Directory.PrivateRunRoot, Is.EqualTo(".e2e-private/runs"));
            Assert.That(runtimeFactory.Request.Directory.CleanupTimeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(runtimeFactory.Request.Database.ConnectionString, Is.SameAs(database.ConnectionString));
            Assert.That(request.ExecutionTimeout, Is.EqualTo(TimeSpan.FromSeconds(900)));
            Assert.That(request.ShutdownTimeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
        });

        await lease!.DisposeAsync();

        Assert.That(cleanupOrder, Is.EqualTo(new[] { "api-process", "runtime-configuration", "postgresql" }));
    }

    [Test]
    public void ExternalApiHost_rehashes_verified_publication_before_process_start()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([], cleanupOrder);
        var readiness = new ScriptedApiHostReadinessMonitor([]);
        File.AppendAllText(fixture.Publication.DllPath, "mutated");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(database),
                fixture.CreateInfrastructure(
                    runtimeFactory,
                    processStarter,
                    readiness,
                    new FakeLoopbackPortAllocator([43126]))));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ApiPublication.IntegrityMessage));
            Assert.That(processStarter.Requests, Is.Empty);
            Assert.That(cleanupOrder, Is.EqualTo(new[] { "runtime-configuration", "postgresql" }));
        });
    }
}
