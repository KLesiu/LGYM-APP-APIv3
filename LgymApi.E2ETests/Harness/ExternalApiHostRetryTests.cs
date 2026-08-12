namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ExternalApiHostRetryTests
{
    [Test]
    public async Task ExternalApiHost_dynamic_port_retries_only_address_conflicts_under_one_deadline()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter(
            [ExternalApiProcessExitKind.AddressInUse, ExternalApiProcessExitKind.AddressInUse, null],
            cleanupOrder);
        var readiness = new ScriptedApiHostReadinessMonitor(
            [ApiHostReadinessOutcome.AddressInUse, ApiHostReadinessOutcome.AddressInUse, ApiHostReadinessOutcome.Ready]);
        var portAllocator = new FakeLoopbackPortAllocator([44101, 44102, 44103]);

        var lease = await ExternalApiHostLease.StartAsync(
            fixture.CreateRequest(database),
            fixture.CreateInfrastructure(runtimeFactory, processStarter, readiness, portAllocator));

        Assert.Multiple(() =>
        {
            Assert.That(portAllocator.AllocationCount, Is.EqualTo(3));
            Assert.That(processStarter.Requests, Has.Count.EqualTo(3));
            Assert.That(processStarter.Processes[0].DisposeCount, Is.EqualTo(1));
            Assert.That(processStarter.Processes[1].DisposeCount, Is.EqualTo(1));
            Assert.That(processStarter.Processes[2].DisposeCount, Is.Zero);
            Assert.That(
                processStarter.StartInfos.Select(info => info.Environment["ASPNETCORE_URLS"]),
                Is.EqualTo(new[]
                {
                    "http://127.0.0.1:44101",
                    "http://127.0.0.1:44102",
                    "http://127.0.0.1:44103"
                }));
            Assert.That(readiness.StartupTokens, Has.All.EqualTo(readiness.StartupTokens[0]));
            Assert.That(readiness.StartupTokens, Has.All.Matches<CancellationToken>(token => token.CanBeCanceled));
        });

        await lease.DisposeAsync();
    }

    [Test]
    public void ExternalApiHost_dynamic_port_stops_after_three_address_conflicts()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter(
            Enumerable.Repeat<ExternalApiProcessExitKind?>(ExternalApiProcessExitKind.AddressInUse, 3),
            cleanupOrder);
        var readiness = new ScriptedApiHostReadinessMonitor(
            Enumerable.Repeat(ApiHostReadinessOutcome.AddressInUse, 3));
        var portAllocator = new FakeLoopbackPortAllocator([44201, 44202, 44203]);

        var exception = Assert.ThrowsAsync<ExternalApiHostStartupException>(() =>
            ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(database),
                fixture.CreateInfrastructure(runtimeFactory, processStarter, readiness, portAllocator)));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalApiHostLease.AddressInUseMessage));
            Assert.That(portAllocator.AllocationCount, Is.EqualTo(3));
            Assert.That(processStarter.Requests, Has.Count.EqualTo(3));
            Assert.That(cleanupOrder, Is.EqualTo(new[]
            {
                "api-process",
                "api-process",
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
        });
    }

    [Test]
    public void ExternalApiHost_fixed_port_never_allocates_or_retries()
    {
        using var fixture = new ExternalApiHostTestFixture();
        fixture.Options.Api.Port = 44321;
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter(
            [ExternalApiProcessExitKind.AddressInUse],
            cleanupOrder);
        var readiness = new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.AddressInUse]);
        var portAllocator = new FakeLoopbackPortAllocator([]);

        var exception = Assert.ThrowsAsync<ExternalApiHostStartupException>(() =>
            ExternalApiHostLease.StartAsync(
                fixture.CreateRequest(database),
                fixture.CreateInfrastructure(runtimeFactory, processStarter, readiness, portAllocator)));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalApiHostLease.AddressInUseMessage));
            Assert.That(portAllocator.AllocationCount, Is.Zero);
            Assert.That(processStarter.Requests, Has.Count.EqualTo(1));
            Assert.That(
                processStarter.StartInfos.Single().Environment["ASPNETCORE_URLS"],
                Is.EqualTo("http://127.0.0.1:44321"));
            Assert.That(cleanupOrder, Is.EqualTo(new[]
            {
                "api-process",
                "runtime-configuration",
                "postgresql"
            }));
        });
    }
}
