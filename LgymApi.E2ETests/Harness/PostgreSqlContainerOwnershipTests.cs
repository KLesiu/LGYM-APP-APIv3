using LgymApi.E2ETests.Lifecycle;
using System.Diagnostics;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class PostgreSqlContainerOwnershipTests
{
    [Test]
    public async Task PostgreSQL_cleanup_uses_one_shared_deadline_for_disposal_and_absence()
    {
        var operations = new ControlledPostgreSqlContainerOperations
        {
            DisposeDelay = TimeSpan.FromMilliseconds(70),
            AbsenceDelay = TimeSpan.FromMilliseconds(70)
        };
        var lease = await PostgreSqlContainerLease.StartAsync(
            operations,
            TimeSpan.FromMilliseconds(100));

        var stopwatch = Stopwatch.StartNew();
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await lease.DisposeAsync());
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Testcontainers PostgreSQL cleanup exceeded the configured shutdown timeout."));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(160)));
            Assert.That(operations.DisposeCount, Is.EqualTo(1));
            Assert.That(operations.AbsenceCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PostgreSQL_cleanup_failure_leaves_lease_retryable_and_serialized()
    {
        var operations = new ControlledPostgreSqlContainerOperations { DisposeFailuresRemaining = 1 };
        var lease = await PostgreSqlContainerLease.StartAsync(operations, TimeSpan.FromSeconds(1));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await lease.DisposeAsync());
        await lease.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Testcontainers PostgreSQL cleanup failed."));
            Assert.That(operations.DisposeCount, Is.EqualTo(2));
            Assert.That(operations.AbsenceCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void PostgreSQL_post_acquisition_start_failure_cleans_the_owned_container_within_one_deadline()
    {
        var operations = new ControlledPostgreSqlContainerOperations { StartFailureAfterAcquisition = true };
        var stopwatch = Stopwatch.StartNew();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => PostgreSqlContainerLease.StartAsync(
            operations,
            TimeSpan.FromMilliseconds(100)));
        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Injected startup failure after acquisition."));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(160)));
            Assert.That(operations.DisposeCount, Is.EqualTo(1));
            Assert.That(operations.AbsenceCount, Is.EqualTo(1));
            Assert.That(operations.IsAbsent, Is.True);
        });
    }
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

    private sealed class ControlledPostgreSqlContainerOperations : IPostgreSqlContainerLeaseOperations
    {
        internal TimeSpan DisposeDelay { get; init; }

        internal TimeSpan AbsenceDelay { get; init; }

        internal int DisposeFailuresRemaining { get; set; }

        internal bool StartFailureAfterAcquisition { get; init; }

        internal int DisposeCount { get; private set; }

        internal int AbsenceCount { get; private set; }

        internal bool IsAbsent { get; private set; }

        public string ConnectionString => "redacted";

        public bool IsRunning => !IsAbsent;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return StartFailureAfterAcquisition
                ? Task.FromException(new InvalidOperationException("Injected startup failure after acquisition."))
                : Task.CompletedTask;
        }

        public async Task DisposeAsync(CancellationToken cancellationToken)
        {
            DisposeCount++;
            await Task.Delay(DisposeDelay, cancellationToken);
            if (DisposeFailuresRemaining > 0)
            {
                DisposeFailuresRemaining--;
                throw new IOException("Injected cleanup failure.");
            }
        }

        public async Task<bool> WaitUntilAbsentAsync(CancellationToken cancellationToken)
        {
            AbsenceCount++;
            await Task.Delay(AbsenceDelay, cancellationToken);
            IsAbsent = true;
            return true;
        }
    }
}
