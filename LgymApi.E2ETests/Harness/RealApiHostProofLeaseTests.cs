using LgymApi.E2ETests.Lifecycle;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Lifecycle")]
[Category("ApiHostProof")]
public sealed class RealApiHostProofLeaseTests
{
    [Test]
    public async Task ApiHostObservation_real_proof_wrapper_retries_after_host_cleanup_failure()
    {
        using var fixture = new ExternalApiHostTestFixture();
        var cleanupOrder = new List<string>();
        var database = new FakeApiHostDatabaseLease(cleanupOrder);
        var runtimeFactory = new FakeApiHostRuntimeFactory(
            fixture.RepositoryRoot,
            cleanupOrder,
            cleanupFails: true);
        var processStarter = new FakeExternalApiProcessStarter(
            [null],
            cleanupOrder,
            cleanupFailures: [true]);
        var host = await ExternalApiHostLease.StartAsync(
            fixture.CreateRequest(database),
            fixture.CreateInfrastructure(
                runtimeFactory,
                processStarter,
                new ScriptedApiHostReadinessMonitor([ApiHostReadinessOutcome.Ready]),
                new FakeLoopbackPortAllocator([46501])));
        var databaseAbsenceChecks = 0;
        var wrapper = new RealApiHostProofLease(
            host,
            new ScenarioResourceObservation(
                ScenarioResourceIdentity.Create(),
                () =>
                {
                    databaseAbsenceChecks++;
                    return Task.FromResult(true);
                }),
            fixture.Options);

        var firstFailure = Assert.ThrowsAsync<ExternalApiHostCleanupException>(async () =>
            await wrapper.DisposeAsync());
        await wrapper.DisposeAsync();
        await wrapper.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstFailure!.Receipt.ProcessTreeAbsent, Is.False);
            Assert.That(firstFailure.Receipt.RuntimeDirectoryAbsent, Is.False);
            Assert.That(wrapper.CleanupReceipt.AllResourcesAbsent, Is.True);
            Assert.That(processStarter.Processes.Single().DisposeCount, Is.EqualTo(2));
            Assert.That(runtimeFactory.Lease!.DisposeCount, Is.EqualTo(2));
            Assert.That(database.DisposeCount, Is.EqualTo(1));
            Assert.That(databaseAbsenceChecks, Is.EqualTo(1));
        });
    }
}
