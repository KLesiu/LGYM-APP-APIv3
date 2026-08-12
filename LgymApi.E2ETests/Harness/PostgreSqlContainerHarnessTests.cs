namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class PostgreSqlContainerHarnessTests
{
    [Test]
    public async Task PostgreSQL_container_starts_with_module_readiness_and_is_removed_on_disposal()
    {
        PostgreSqlContainerLease? lease = null;

        try
        {
            lease = await PostgreSqlContainerLease.StartAsync();

            Assert.Multiple(() =>
            {
                Assert.That(lease.IsRunning, Is.True);
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
