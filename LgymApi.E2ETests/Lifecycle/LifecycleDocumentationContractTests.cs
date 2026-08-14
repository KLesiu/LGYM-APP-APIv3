using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

[TestFixture]
[Category("Lifecycle")]
public sealed class LifecycleDocumentationContractTests
{
    private const string SourcePin = "8f59d96ec368f509b1565e3296cd89d2a082a952";
    private const string HarnessOnlyCommand = "pwsh -NoProfile -File LgymApi.E2ETests/scripts/invoke-e2e-coordinator.ps1 -Mode HarnessOnly";

    [Test]
    public void Durable_documentation_matches_the_per_scenario_lifecycle_contract()
    {
        AssertDocumentationContract(ReadDocumentation(RepositoryRoot.Find()));
    }

    [Test]
    public void Documentation_contract_rejects_a_stale_private_readiness_fixture()
    {
        using var fixture = DocumentationFixture.Create(ReadDocumentation(RepositoryRoot.Find()));
        fixture.ReplaceE2ETests(
            "public `POST /api/login` invalid-login gate returning exactly `401 Unauthorized`",
            "private readiness endpoint");

        Assert.That(() => AssertDocumentationContract(fixture.Read()), Throws.TypeOf<MultipleAssertException>());
    }

    [Test]
    public void Documentation_contract_rejects_a_missing_category_and_artifact_boundary_fixture()
    {
        using var fixture = DocumentationFixture.Create(ReadDocumentation(RepositoryRoot.Find()));
        fixture.ReplaceE2ETests(
            "`HarnessDocker` and `Lifecycle` are nonempty, disjoint categories.",
            "Lifecycle runs cover the required checks.");
        fixture.ReplaceE2ETests(
            "Failure-only artifacts are bounded sanitized receipts",
            "Artifacts are retained for every scenario");

        Assert.That(() => AssertDocumentationContract(fixture.Read()), Throws.TypeOf<MultipleAssertException>());
    }

    [Test]
    public void Documentation_contract_rejects_a_successor_coordinator_fixture()
    {
        using var fixture = DocumentationFixture.Create(ReadDocumentation(RepositoryRoot.Find()));
        fixture.ReplaceE2ETests(
            "`HarnessOnly` is the only coordinator mode.",
            "`Full` is the coordinator mode.");

        Assert.That(() => AssertDocumentationContract(fixture.Read()), Throws.TypeOf<MultipleAssertException>());
    }

    [Test]
    public void Documentation_contract_rejects_divergent_agent_files_in_a_fixture()
    {
        using var fixture = DocumentationFixture.Create(ReadDocumentation(RepositoryRoot.Find()));
        fixture.ReplaceAgent("Approved E2E browser lifecycle probes are allowed", "Browser lifecycle probes are prohibited");

        Assert.That(() => AssertDocumentationContract(fixture.Read()), Throws.TypeOf<MultipleAssertException>());
    }

    private static void AssertDocumentationContract(Documentation documents)
    {
        Assert.Multiple(() =>
        {
            Assert.That(documents.E2ETests, Does.Contain(SourcePin));
            Assert.That(documents.E2ETests, Does.Contain("one `npm ci` installation"));
            Assert.That(documents.E2ETests, Does.Contain("fresh scenario stack in this order: randomized PostgreSQL Testcontainers lease, external published API process and runtime configuration, public `GET /health/live`, public `POST /api/login` invalid-login gate returning exactly `401 Unauthorized`, Expo Web process, Playwright Chromium process, browser context, and page"));
            Assert.That(documents.E2ETests, Does.Contain("`Timeouts.ScenarioSeconds` token bounds every scenario acquisition and probe"));
            Assert.That(documents.E2ETests, Does.Contain("page/context, Chromium/Playwright, Expo process, API process/runtime configuration/PostgreSQL, then scenario runtime children"));
            Assert.That(documents.E2ETests, Does.Contain("`HarnessDocker` and `Lifecycle` are nonempty, disjoint categories."));
            Assert.That(documents.E2ETests, Does.Contain(HarnessOnlyCommand));
            Assert.That(documents.E2ETests, Does.Contain("Failure-only artifacts are bounded sanitized receipts at `.e2e-private/runs/<run-id>/artifacts/<safe-scenario-id>/`."));
            Assert.That(documents.E2ETests, Does.Contain("Issue `#436` owns product business scenarios"));
            Assert.That(documents.E2ETests, Does.Contain("Issue `#437` owns the `Full` coordinator mode and `ArtifactDrill`"));
            Assert.That(documents.E2ETests, Does.Contain("`HarnessOnly` is the only coordinator mode."));
            Assert.That(documents.Architecture, Does.Contain("may run the approved external Expo and browser lifecycle probes"));
            Assert.That(documents.Architecture, Does.Contain("Product authentication, onboarding, and other business scenarios remain deferred to `#436`."));
            Assert.That(documents.Architecture, Does.Contain("`Full` and `ArtifactDrill` remain deferred to `#437`."));
            Assert.That(documents.Agent, Is.EqualTo(documents.Agents));
            Assert.That(documents.Agent, Does.Contain("Approved E2E browser lifecycle probes are allowed"));
            Assert.That(documents.Agent, Does.Contain("private failure artifacts ignored, sanitized, and never committed"));
        });
    }

    private static Documentation ReadDocumentation(string repositoryRoot) => new(
        File.ReadAllText(Path.Combine(repositoryRoot, "LgymApi.E2ETests", "LgymApi.E2ETests.md")),
        File.ReadAllText(Path.Combine(repositoryRoot, "docs", "ARCHITECTURE.md")),
        File.ReadAllText(Path.Combine(repositoryRoot, "AGENT.md")),
        File.ReadAllText(Path.Combine(repositoryRoot, "AGENTS.md")));

    private sealed record Documentation(string E2ETests, string Architecture, string Agent, string Agents);

    private sealed class DocumentationFixture : IDisposable
    {
        private readonly string _root;

        private DocumentationFixture(string root)
        {
            _root = root;
        }

        internal static DocumentationFixture Create(Documentation documentation)
        {
            var root = Directory.CreateTempSubdirectory("lgym-e2e-lifecycle-docs-").FullName;
            Directory.CreateDirectory(Path.Combine(root, "LgymApi.E2ETests"));
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "LgymApi.E2ETests", "LgymApi.E2ETests.md"), documentation.E2ETests);
            File.WriteAllText(Path.Combine(root, "docs", "ARCHITECTURE.md"), documentation.Architecture);
            File.WriteAllText(Path.Combine(root, "AGENT.md"), documentation.Agent);
            File.WriteAllText(Path.Combine(root, "AGENTS.md"), documentation.Agents);
            return new DocumentationFixture(root);
        }

        internal void ReplaceE2ETests(string oldValue, string newValue) => Replace(
            Path.Combine(_root, "LgymApi.E2ETests", "LgymApi.E2ETests.md"), oldValue, newValue);

        internal void ReplaceAgent(string oldValue, string newValue) => Replace(
            Path.Combine(_root, "AGENT.md"), oldValue, newValue);

        internal Documentation Read() => ReadDocumentation(_root);

        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);
        }

        private static void Replace(string path, string oldValue, string newValue) => File.WriteAllText(
            path,
            File.ReadAllText(path).Replace(oldValue, newValue, StringComparison.Ordinal));
    }
}
