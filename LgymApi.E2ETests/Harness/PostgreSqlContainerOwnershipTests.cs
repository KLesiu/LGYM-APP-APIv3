using LgymApi.E2ETests.Lifecycle;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class PostgreSqlContainerOwnershipTests
{
    [Test]
    public async Task PostgreSQL_scenario_ownership_disposes_once_before_transfer()
    {
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var ownership = new ScenarioDatabaseOwnership(database, CreateObservation());

        await ownership.DisposeAsync();
        await ownership.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(["postgresql"]));
        });
    }

    [Test]
    public async Task PostgreSQL_scenario_ownership_clears_its_disposable_slot_before_host_startup()
    {
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var ownership = new ScenarioDatabaseOwnership(database, CreateObservation());

        var transferred = ownership.TransferToApiHost();
        await ownership.DisposeAsync();
        await transferred.DisposeAsync();
        await ownership.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(["postgresql"]));
        });
    }

    [Test]
    public async Task PostgreSQL_transferred_to_API_host_is_disposed_once_after_successful_start()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        await using var ownership = new ScenarioDatabaseOwnership(database, CreateObservation());
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([null], cleanupOrder);

        var hostDatabase = ownership.TransferToApiHost();
        var host = await ExternalApiHostLease.StartAsync(
            new ExternalApiHostCompositionRequest(
                fixture.Publication,
                hostDatabase,
                fixture.Options,
                fixture.RepositoryRoot),
            fixture.CreateInfrastructure(
                runtimeFactory,
                processStarter,
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([46401])));

        await ownership.DisposeAsync();
        await host.DisposeAsync();
        await ownership.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(["api-process", "runtime-configuration", "postgresql"]));
        });
    }

    [Test]
    public async Task PostgreSQL_transferred_to_API_host_is_disposed_once_when_startup_fails()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        await using var ownership = new ScenarioDatabaseOwnership(database, CreateObservation());
        var runtimeFactory = new FakeApiHostRuntimeFactory(fixture.RepositoryRoot, cleanupOrder);
        var processStarter = new FakeExternalApiProcessStarter([null], cleanupOrder);

        var hostDatabase = ownership.TransferToApiHost();
        var exception = Assert.ThrowsAsync<ExternalApiHostStartupException>(() =>
            ExternalApiHostLease.StartAsync(
                new ExternalApiHostCompositionRequest(
                    fixture.Publication,
                    hostDatabase,
                    fixture.Options,
                    fixture.RepositoryRoot),
                fixture.CreateInfrastructure(
                    runtimeFactory,
                    processStarter,
                    new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.HttpFailure]),
                    new FakeLoopbackPortAllocator([46402]))));

        await ownership.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(ExternalApiHostLease.StartupFailureMessage));
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(cleanupOrder, Is.EqualTo(["api-process", "runtime-configuration", "postgresql"]));
        });
    }

    [Test]
    public void Scenario_resource_identities_have_value_equality_without_raw_text()
    {
        var first = ScenarioResourceIdentity.Create();
        var second = ScenarioResourceIdentity.Create();

        Assert.Multiple(() =>
        {
            Assert.That(first.Equals(first), Is.True);
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first.ToString(), Is.EqualTo("<scenario-resource-identity>"));
            Assert.That(second.ToString(), Is.EqualTo("<scenario-resource-identity>"));
        });
    }

    private static ScenarioResourceObservation CreateObservation() =>
        new(ScenarioResourceIdentity.Create(), () => Task.FromResult(true));
}
