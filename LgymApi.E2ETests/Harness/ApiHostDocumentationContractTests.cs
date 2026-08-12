namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class ApiHostDocumentationContractTests
{
    private const string FocusedCommand = "dotnet test LgymApi.E2ETests/LgymApi.E2ETests.csproj --configuration Release --no-build --settings LgymApi.E2ETests/LgymApi.E2ETests.runsettings --filter \"TestCategory=ApiHostProof\" --logger \"trx;LogFileName=issue-433-api-host.trx\"";

    [Test]
    public void Durable_E2E_documentation_matches_the_external_API_host_contract()
    {
        AssertDocumentationContract(ReadDocumentation(RepositoryRoot.Find()));
    }

    [Test]
    public void Documentation_contract_rejects_stale_non_host_wording_in_a_fixture()
    {
        var documents = ReadDocumentation(RepositoryRoot.Find()) with
        {
            E2ETests = "The harness does not start the API."
        };

        Assert.That(() => AssertDocumentationContract(documents), Throws.TypeOf<MultipleAssertException>());
    }

    [Test]
    public void Documentation_contract_rejects_a_missing_ApiHostProof_filter_in_a_fixture()
    {
        var documents = ReadDocumentation(RepositoryRoot.Find()) with
        {
            E2ETests = ReadDocumentation(RepositoryRoot.Find()).E2ETests.Replace(
                "--filter \"TestCategory=ApiHostProof\" ",
                string.Empty,
                StringComparison.Ordinal)
        };

        Assert.That(() => AssertDocumentationContract(documents), Throws.TypeOf<MultipleAssertException>());
    }

    [Test]
    public void Documentation_contract_rejects_divergent_agent_E2E_rows_in_a_fixture()
    {
        var documents = ReadDocumentation(RepositoryRoot.Find()) with
        {
            Agent = ReadDocumentation(RepositoryRoot.Find()).Agent.Replace(
                "external published-API/process/public-HTTP proofs",
                "stale proof wording",
                StringComparison.Ordinal)
        };

        Assert.That(() => AssertDocumentationContract(documents), Throws.TypeOf<MultipleAssertException>());
    }

    private static void AssertDocumentationContract(Documentation documents)
    {
        Assert.Multiple(() =>
        {
            Assert.That(documents.Api, Does.Contain("| Testing | Npgsql registration | skipped | skipped | disabled | suppressed | no-op |"));
            Assert.That(documents.Api, Does.Contain("| E2E | Npgsql | apply normal EF migrations | enabled after migration | enabled | suppressed | no-op |"));
            Assert.That(documents.Api, Does.Contain("http://localhost:8083"));
            Assert.That(documents.E2ETests, Does.Not.Contain("does not start the API"));
            Assert.That(documents.E2ETests, Does.Contain("external published API process"));
            Assert.That(documents.E2ETests, Does.Contain(FocusedCommand));
            Assert.That(documents.E2ETests, Does.Contain("api-process, runtime-configuration, postgresql"));
            Assert.That(documents.Architecture, Does.Contain("E2E is the only non-Development automatic-migration exception"));
            Assert.That(GetE2ETestsRow(documents.Agent), Is.EqualTo(GetE2ETestsRow(documents.Agents)));
            Assert.That(documents.Agent, Does.Contain(FocusedCommand));
            Assert.That(documents.Agents, Does.Contain(FocusedCommand));
        });
    }

    private static Documentation ReadDocumentation(string repositoryRoot) => new(
        File.ReadAllText(Path.Combine(repositoryRoot, "LgymApi.Api", "LgymApi.Api.md")),
        File.ReadAllText(Path.Combine(repositoryRoot, "LgymApi.E2ETests", "LgymApi.E2ETests.md")),
        File.ReadAllText(Path.Combine(repositoryRoot, "docs", "ARCHITECTURE.md")),
        File.ReadAllText(Path.Combine(repositoryRoot, "AGENT.md")),
        File.ReadAllText(Path.Combine(repositoryRoot, "AGENTS.md")));

    private static string GetE2ETestsRow(string document) => document.Split('\n')
        .Single(line => line.StartsWith("| `LgymApi.E2ETests/LgymApi.E2ETests.csproj`", StringComparison.Ordinal));

    private sealed record Documentation(string Api, string E2ETests, string Architecture, string Agent, string Agents);
}
