using LgymApi.E2ETests.Lifecycle;
using System.Reflection;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
[Category("HarnessDocker")]
public sealed class PostgreSqlContainerHarnessTests
{
    [Test]
    public void PostgreSQL_observation_contract_is_non_disposable_and_redacted()
    {
        var observationType = typeof(PostgreSqlContainerLease).Assembly.GetType(
            "LgymApi.E2ETests.Lifecycle.ScenarioResourceObservation");
        var identityType = typeof(PostgreSqlContainerLease).Assembly.GetType(
            "LgymApi.E2ETests.Lifecycle.ScenarioResourceIdentity");

        Assert.Multiple(() =>
        {
            Assert.That(typeof(PostgreSqlContainerLease).GetMethod(
                "CreateObservation",
                BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
            Assert.That(observationType, Is.Not.Null);
            Assert.That(identityType, Is.Not.Null);
            Assert.That(observationType!.GetInterfaces(), Does.Not.Contain(typeof(IAsyncDisposable)));
            Assert.That(observationType.GetMethod("DisposeAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), Is.Null);
            Assert.That(observationType.GetMethod("ConfirmAbsentAsync", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
            Assert.That(identityType!.GetMethod("ToString", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
        });
    }

    [Test]
    public async Task PostgreSQL_container_starts_with_module_readiness_and_is_removed_on_disposal()
    {
        PostgreSqlContainerLease? lease = null;

        try
        {
            lease = await PostgreSqlContainerLease.StartAsync();
            var observation = lease.CreateObservation();
            var repeatedObservation = lease.CreateObservation();

            Assert.Multiple(() =>
            {
                Assert.That(lease.IsRunning, Is.True);
                Assert.That(observation.Identity, Is.EqualTo(repeatedObservation.Identity));
                Assert.That(observation.Identity.ToString(), Is.EqualTo("<scenario-resource-identity>"));
                Assert.That(observation.ToString(), Is.EqualTo("<scenario-resource-observation>"));
            });
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
            }
        }

        Assert.That(lease!.CleanupReceipt.ContainerAbsent, Is.True);
        Assert.That(await lease.ConfirmAbsentAsync(), Is.True);
        Assert.That(await lease.CreateObservation().ConfirmAbsentAsync(), Is.True);
        WriteCleanupReceipt(lease);
    }

    [Test]
    public void PostgreSQL_container_is_removed_when_a_test_local_failure_occurs_after_start()
    {
        PostgreSqlContainerLease? lease = null;

        var exception = Assert.ThrowsAsync<InjectedPostStartFailureException>(async () =>
        {
            lease = await PostgreSqlContainerLease.StartAsync();
            try
            {
                throw new InjectedPostStartFailureException();
            }
            finally
            {
                await lease.DisposeAsync();
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<InjectedPostStartFailureException>());
            Assert.That(exception!.Message, Is.EqualTo("Injected post-start lifecycle failure."));
            Assert.That(lease, Is.Not.Null);
            Assert.That(lease!.CleanupReceipt.ContainerAbsent, Is.True);
        });
        WriteCleanupReceipt(lease!);
    }

    [Test]
    public async Task PostgreSQL_sequential_leases_have_distinct_redacted_observations_and_are_absent()
    {
        var first = await PostgreSqlContainerLease.StartAsync();
        var firstObservation = first.CreateObservation();
        await first.DisposeAsync();

        var second = await PostgreSqlContainerLease.StartAsync();
        var secondObservation = second.CreateObservation();
        await second.DisposeAsync();
        var firstAbsent = await firstObservation.ConfirmAbsentAsync();
        var secondAbsent = await secondObservation.ConfirmAbsentAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstObservation.Identity, Is.Not.EqualTo(secondObservation.Identity));
            Assert.That(firstObservation.Identity.ToString(), Is.EqualTo("<scenario-resource-identity>"));
            Assert.That(secondObservation.Identity.ToString(), Is.EqualTo("<scenario-resource-identity>"));
            Assert.That(firstAbsent, Is.True);
            Assert.That(secondAbsent, Is.True);
        });
    }

    private static void WriteCleanupReceipt(PostgreSqlContainerLease lease)
    {
        TestContext.Out.WriteLine(
            $"Task 4 PostgreSQL cleanup: category={lease.CleanupReceipt.Category}; absent={lease.CleanupReceipt.ContainerAbsent}; durationMilliseconds={(long)lease.CleanupReceipt.Duration.TotalMilliseconds}.");
    }

    private sealed class InjectedPostStartFailureException : Exception
    {
        public InjectedPostStartFailureException()
            : base("Injected post-start lifecycle failure.")
        {
        }
    }
}
