namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class RuntimeConfigurationContractTests
{
    [Test]
    public void PostgreSQL_lease_exposes_only_internal_runtime_details()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(PostgreSqlContainerLease).GetProperty("ContainerId"), Is.Null);
            Assert.That(typeof(PostgreSqlContainerLease).GetProperty("MappedPort"), Is.Null);
            Assert.That(typeof(PostgreSqlContainerLease).GetProperty("ConnectionString"), Is.Null);
        });
    }

    [Test]
    public void Runtime_configuration_lease_is_available_as_a_typed_host_contract()
    {
        Assert.That(typeof(RuntimeConfigurationLease).ToString(), Is.EqualTo("LgymApi.E2ETests.Harness.RuntimeConfigurationLease"));
    }
}
