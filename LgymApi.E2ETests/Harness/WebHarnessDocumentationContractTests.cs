namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class WebHarnessDocumentationContractTests
{
    [Test]
    public void Documentation_describes_the_current_issue_436_browser_harness()
    {
        AssertDocumentationContract(ReadDocumentation());
    }

    [Test]
    public void Documentation_contract_rejects_stale_browser_and_source_claims()
    {
        var document = ReadDocumentation().Replace("launches the published API only", "does not launch a browser or access a source checkout", StringComparison.Ordinal);

        Assert.That(() => AssertDocumentationContract(document), Throws.TypeOf<MultipleAssertException>());
    }

    private static void AssertDocumentationContract(string document)
    {
        Assert.Multiple(() =>
        {
            Assert.That(document, Does.Contain("Windows"));
            Assert.That(document, Does.Contain("Node `>= 22.18`"));
            Assert.That(document, Does.Contain("install-playwright-chromium.ps1"));
            Assert.That(document, Does.Contain("BROWSER=none"));
            Assert.That(document, Does.Contain("private HOME/USERPROFILE/TEMP/TMP"));
            Assert.That(document, Does.Contain("REACT_APP_BACKEND"));
            Assert.That(document, Does.Contain("8083"));
            Assert.That(document, Does.Contain("six live source-pinned locator surfaces"));
            Assert.That(document, Does.Contain("#436"));
            Assert.That(document, Does.Not.Contain("does not launch a browser"));
            Assert.That(document, Does.Not.Contain("does not access a source checkout"));
        });
    }

    private static string ReadDocumentation() => File.ReadAllText(Path.Combine(
        RepositoryRoot.Find(), "LgymApi.E2ETests", "LgymApi.E2ETests.md"));
}
