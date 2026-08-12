using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ApiPublicationConfigurationCharacterizationTests
{
    [Test]
    public void Committed_API_publication_configuration_identifies_the_canonical_DLL_and_bound()
    {
        // Given: the committed E2E configuration copied to the standalone test output.
        var repositoryRoot = RepositoryRoot.Find();

        // When: the validated configuration is loaded.
        var options = E2EConfiguration.Load(TestContext.CurrentContext.TestDirectory, repositoryRoot);

        // Then: Task 3 has one canonical DLL identity and one bounded publication timeout.
        Assert.Multiple(() =>
        {
            Assert.That(
                options.Api.PublishedDllPath.Replace('\\', '/'),
                Is.EqualTo(".e2e-private/published-api/LgymApi.Api.dll"));
            Assert.That(options.Timeouts.ApiPublishSeconds, Is.EqualTo(300));
        });
    }
}
