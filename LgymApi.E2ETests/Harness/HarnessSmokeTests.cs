namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
public sealed class HarnessSmokeTests
{
    [Test]
    public void Safe_e2e_configuration_should_be_available_in_test_output()
    {
        // Given: the standalone test output directory.
        var configurationPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "appsettings.E2E.json");

        // When: the Harness checks for its safe configuration artifact.
        var configurationExists = File.Exists(configurationPath);

        // Then: later configuration work must copy the committed safe artifact to the output.
        Assert.That(
            configurationExists,
            Is.True,
            "Safe E2E configuration must be copied to the test output.");
    }
}
