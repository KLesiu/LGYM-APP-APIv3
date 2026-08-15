using System.Text.Json;
using System.Text.Json.Nodes;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Lifecycle")]
public sealed class FinalLifecycleEvidenceManifestTests
{
    [Test]
    public void FinalLifecycleEvidence_preserves_only_the_complete_safe_schema_v1_contract()
    {
        var manifest = CreateValidManifest();
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        var lifecycleRun = root.GetProperty("lifecycle").GetProperty("run");

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo(FinalLifecycleEvidenceManifest.Schema));
            Assert.That(root.GetProperty("api").GetProperty("headSha").GetString(), Is.EqualTo(HeadSha));
            Assert.That(root.GetProperty("api").GetProperty("repositoryDirty").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("harnessDocker").GetProperty("counters").GetProperty("passed").GetInt32(),
                Is.EqualTo(FinalLifecycleEvidenceManifest.RequiredHarnessDockerContracts.Length));
            Assert.That(lifecycleRun.GetProperty("completedScenarioCount").GetInt32(), Is.EqualTo(2));
            Assert.That(lifecycleRun.GetProperty("scenarios").GetArrayLength(), Is.EqualTo(2));
            Assert.That(lifecycleRun.GetProperty("runtimeRootAbsent").GetBoolean(), Is.True);
            Assert.That(lifecycleRun.GetProperty("successArtifactsAbsent").GetBoolean(), Is.True);
            Assert.That(manifest, Does.Not.Contain("raw-user-canary"));
            Assert.That(manifest, Does.Not.Contain("raw-machine-canary"));
            Assert.That(manifest, Does.Not.Contain("raw-private-path-canary"));
            Assert.That(manifest, Does.Not.Contain("raw-secret-canary"));
            Assert.That(manifest, Does.Not.Contain("raw-identity-canary"));
            Assert.That(manifest, Does.Not.Contain("jwt-canary"));
            Assert.That(manifest, Does.Not.Contain("cookie-canary"));
            Assert.That(manifest, Does.Not.Contain("storage-canary"));
        });
    }

    [TestCase("zero")]
    [TestCase("timeout")]
    [TestCase("skipped")]
    [TestCase("failed")]
    [TestCase("missing-contract")]
    [TestCase("duplicate-contract")]
    [TestCase("predecessor-name")]
    [TestCase("malformed")]
    public void FinalLifecycleEvidence_rejects_invalid_or_predecessor_TRX_evidence(string mutation)
    {
        var harnessDocker = CreateTrx(FinalLifecycleEvidenceManifest.RequiredHarnessDockerContracts);
        var lifecycle = CreateTrx(FinalLifecycleEvidenceManifest.RequiredLifecycleContracts);
        switch (mutation)
        {
            case "zero":
                harnessDocker = CreateTrx([]);
                break;
            case "timeout":
                lifecycle = CreateTrx(FinalLifecycleEvidenceManifest.RequiredLifecycleContracts, timeout: 1);
                break;
            case "skipped":
                lifecycle = CreateTrx(FinalLifecycleEvidenceManifest.RequiredLifecycleContracts, outcome: "NotExecuted");
                break;
            case "failed":
                lifecycle = CreateTrx(FinalLifecycleEvidenceManifest.RequiredLifecycleContracts, outcome: "Failed");
                break;
            case "missing-contract":
                harnessDocker = CreateTrx(FinalLifecycleEvidenceManifest.RequiredHarnessDockerContracts[..^1]);
                break;
            case "duplicate-contract":
                lifecycle = CreateTrx([
                    .. FinalLifecycleEvidenceManifest.RequiredLifecycleContracts,
                    FinalLifecycleEvidenceManifest.RequiredLifecycleContracts[0]
                ]);
                break;
            case "predecessor-name":
                lifecycle = CreateTrx([
                    .. FinalLifecycleEvidenceManifest.RequiredLifecycleContracts,
                    "Pinned_source_is_exported_started_and_navigated_by_Chromium"
                ]);
                break;
            case "malformed":
                lifecycle = "<TestRun><ResultSummary><Counters total=\"one\" /></ResultSummary></TestRun>";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Assert.That(
            () => FinalLifecycleEvidenceManifest.Create(
                harnessDocker,
                lifecycle,
                Serialize(ValidDockerReceipt()),
                Serialize(ValidLifecycleReceipt()),
                ValidPublication()),
            Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase("unknown-docker-field")]
    [TestCase("docker-private-path")]
    [TestCase("docker-retained-container")]
    [TestCase("stale-schema")]
    [TestCase("stale-sha")]
    [TestCase("duplicate-case")]
    [TestCase("missing-case")]
    [TestCase("cleanup-failure")]
    [TestCase("retained-root")]
    [TestCase("source-not-preserved")]
    [TestCase("storage-not-isolated")]
    [TestCase("scenario-paths-retained")]
    [TestCase("unsafe-jwt")]
    [TestCase("unsafe-cookie")]
    [TestCase("unsafe-storage")]
    [TestCase("raw-identity")]
    [TestCase("absolute-path")]
    public void FinalLifecycleEvidence_rejects_unsafe_or_incomplete_receipts(string mutation)
    {
        var docker = JsonNode.Parse(Serialize(ValidDockerReceipt()))!.AsObject();
        var lifecycle = JsonNode.Parse(Serialize(ValidLifecycleReceipt()))!.AsObject();
        switch (mutation)
        {
            case "unknown-docker-field":
                docker["unknown"] = true;
                break;
            case "docker-private-path":
                docker["privatePath"] = "C:\\raw-private-path-canary";
                break;
            case "docker-retained-container":
                docker["allContainersAbsent"] = false;
                break;
            case "stale-schema":
                lifecycle["schema"] = "issue-434-web-evidence-v1";
                break;
            case "stale-sha":
                lifecycle["apiHeadSha"] = new string('b', 40);
                break;
            case "duplicate-case":
                lifecycle["scenarios"]![1]!["caseId"] = "lifecycle-probe-a";
                break;
            case "missing-case":
                lifecycle["scenarios"]!.AsArray().RemoveAt(1);
                break;
            case "cleanup-failure":
                lifecycle["scenarios"]![0]!["cleanupFailureCount"] = 1;
                break;
            case "retained-root":
                lifecycle["runtimeRootAbsent"] = false;
                break;
            case "source-not-preserved":
                lifecycle["sourceStatePreserved"] = false;
                break;
            case "storage-not-isolated":
                lifecycle["scenarios"]![0]!["browserStorageEmpty"] = false;
                break;
            case "scenario-paths-retained":
                lifecycle["scenarios"]![0]!["scenarioPathsAbsent"] = false;
                break;
            case "unsafe-jwt":
                lifecycle["jwt"] = "jwt-canary";
                break;
            case "unsafe-cookie":
                lifecycle["cookie"] = "cookie-canary";
                break;
            case "unsafe-storage":
                lifecycle["storage"] = "storage-canary";
                break;
            case "raw-identity":
                lifecycle["rawIdentity"] = "raw-identity-canary";
                break;
            case "absolute-path":
                lifecycle["path"] = "C:\\raw-private-path-canary";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Assert.That(
            () => FinalLifecycleEvidenceManifest.Create(
                CreateTrx(FinalLifecycleEvidenceManifest.RequiredHarnessDockerContracts),
                CreateTrx(FinalLifecycleEvidenceManifest.RequiredLifecycleContracts),
                docker.ToJsonString(),
                lifecycle.ToJsonString(),
                ValidPublication()),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void FinalLifecycleEvidence_requires_canonical_case_ids_in_the_run_receipt_not_TRX_text()
    {
        var lifecycleReceipt = JsonNode.Parse(Serialize(ValidLifecycleReceipt()))!.AsObject();
        lifecycleReceipt["scenarios"]!.AsArray().RemoveAt(1);
        var trx = CreateTrx([
            .. FinalLifecycleEvidenceManifest.RequiredLifecycleContracts,
            "lifecycle-probe-a",
            "lifecycle-probe-b"
        ]);

        Assert.That(
            () => FinalLifecycleEvidenceManifest.Create(
                CreateTrx(FinalLifecycleEvidenceManifest.RequiredHarnessDockerContracts),
                trx,
                Serialize(ValidDockerReceipt()),
                lifecycleReceipt.ToJsonString(),
                ValidPublication()),
            Throws.TypeOf<InvalidOperationException>());
    }

    private const string HeadSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static string CreateValidManifest() => FinalLifecycleEvidenceManifest.Create(
        CreateTrx(FinalLifecycleEvidenceManifest.RequiredHarnessDockerContracts),
        CreateTrx(FinalLifecycleEvidenceManifest.RequiredLifecycleContracts),
        Serialize(ValidDockerReceipt()),
        Serialize(ValidLifecycleReceipt()),
        ValidPublication());

    private static ApiPublicationReceipt ValidPublication() => new(
        "publish",
        new string('b', 64),
        DateTimeOffset.UtcNow,
        HeadSha,
        false,
        new ApiPublicationProcessReceipt(0, false, false));

    private static FinalLifecycleDockerReceipt ValidDockerReceipt() => new(4, 4, true, true, true);

    private static FinalLifecycleRunReceipt ValidLifecycleReceipt() => new(
        FinalLifecycleEvidenceManifest.LifecycleReceiptSchema,
        HeadSha,
        false,
        2,
        true,
        true,
        true,
        [ValidScenario("lifecycle-probe-a"), ValidScenario("lifecycle-probe-b")]);

    private static FinalLifecycleScenarioReceipt ValidScenario(string caseId) => new(
        caseId,
        ["scenario-paths", "postgresql", "external-api-host", "expo", "browser-run", "browser-scenario"],
        ["browser-scenario", "browser-run", "expo", "external-api-host", "scenario-paths"],
        0,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true);

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    private static string CreateTrx(
        IReadOnlyList<string> testNames,
        string outcome = "Passed",
        int timeout = 0)
    {
        var results = string.Join(string.Empty, testNames.Select(name =>
            $"<UnitTestResult testName=\"{name}\" outcome=\"{outcome}\" />"));
        var passed = outcome == "Passed" ? testNames.Count : 0;
        var notExecuted = outcome == "NotExecuted" ? testNames.Count : 0;
        var failed = outcome == "Failed" ? testNames.Count : 0;
        return $"<TestRun runUser=\"raw-user-canary\" computerName=\"raw-machine-canary\"><TestDefinitions storage=\"raw-private-path-canary\" codeBase=\"C:\\raw-private-path-canary\" /><ResultSummary><Counters total=\"{testNames.Count}\" executed=\"{testNames.Count - notExecuted}\" passed=\"{passed}\" failed=\"{failed}\" timeout=\"{timeout}\" notExecuted=\"{notExecuted}\" /></ResultSummary><Results>{results}</Results><Output>raw-secret-canary lifecycle-probe-a lifecycle-probe-b</Output></TestRun>";
    }
}
