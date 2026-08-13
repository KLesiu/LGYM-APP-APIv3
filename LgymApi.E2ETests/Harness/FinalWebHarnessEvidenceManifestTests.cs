using System.Text.Json;
using System.Text.Json.Nodes;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class FinalWebHarnessEvidenceManifestTests
{
    [Test]
    public void EvidenceManifest_preserves_only_the_complete_sanitized_web_contract()
    {
        var manifest = FinalWebHarnessEvidenceManifest.Create(
            CreateTrx(FinalWebHarnessEvidenceManifest.RequiredWebHarnessTests),
            CreateTrx(FinalWebHarnessEvidenceManifest.RequiredLocatorContractTests),
            ValidReceipt());
        using var document = JsonDocument.Parse(manifest);

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo("issue-434-web-evidence-v1"));
            Assert.That(document.RootElement.GetProperty("webHarness").GetProperty("tests").GetArrayLength(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("locatorContract").GetProperty("tests").GetArrayLength(), Is.EqualTo(3));
            Assert.That(document.RootElement.GetProperty("receipts").GetProperty("surfaceCount").GetInt32(), Is.EqualTo(6));
            Assert.That(document.RootElement.GetProperty("receipts").GetProperty("renderedReady").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("receipts").GetProperty("browserSuppressed").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("receipts").GetProperty("portWasAvailableBeforeStart").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("receipts").GetProperty("publicHttpBoundaryUsed").GetBoolean(), Is.True);
            Assert.That(document.RootElement.GetProperty("receipts").GetProperty("cleanup").GetProperty("expoProcessTreeAbsent").GetBoolean(), Is.True);
            Assert.That(manifest, Does.Not.Contain("raw-user-canary"));
            Assert.That(manifest, Does.Not.Contain("raw-private-path-canary"));
            Assert.That(manifest, Does.Not.Contain("raw-secret-canary"));
        });
    }

    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("skipped")]
    [TestCase("zero")]
    public void EvidenceManifest_rejects_invalid_canonical_TRX_results(string mutation)
    {
        var webHarness = CreateTrx(FinalWebHarnessEvidenceManifest.RequiredWebHarnessTests);
        var locatorContract = CreateTrx(FinalWebHarnessEvidenceManifest.RequiredLocatorContractTests);
        switch (mutation)
        {
            case "missing":
                locatorContract = CreateTrx(FinalWebHarnessEvidenceManifest.RequiredLocatorContractTests[..^1]);
                break;
            case "duplicate":
                webHarness = CreateTrx([.. FinalWebHarnessEvidenceManifest.RequiredWebHarnessTests, FinalWebHarnessEvidenceManifest.RequiredWebHarnessTests[0]]);
                break;
            case "skipped":
                locatorContract = CreateTrx(FinalWebHarnessEvidenceManifest.RequiredLocatorContractTests, "NotExecuted");
                break;
            case "zero":
                webHarness = CreateTrx([]);
                break;
        }

        Assert.That(
            () => FinalWebHarnessEvidenceManifest.Create(webHarness, locatorContract, ValidReceipt()),
            Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase("stale-pin")]
    [TestCase("private-pin")]
    [TestCase("incomplete-cleanup")]
    [TestCase("wrong-inventory")]
    [TestCase("not-rendered")]
    [TestCase("browser-not-suppressed")]
    [TestCase("occupied-port")]
    [TestCase("non-public-boundary")]
    [TestCase("process-tree-retained")]
    [TestCase("drain-incomplete")]
    [TestCase("inspection-incomplete")]
    [TestCase("staged-source-retained")]
    [TestCase("npm-cache-retained")]
    [TestCase("browser-run-retained")]
    public void EvidenceManifest_rejects_stale_private_or_incomplete_receipts(string mutation)
    {
        var receipt = mutation switch
        {
            "stale-pin" => ValidReceipt() with { CommitSha = new string('a', 40) },
            "private-pin" => ValidReceipt() with { CommitSha = "raw-private-path-canary" },
            "incomplete-cleanup" => ValidReceipt() with { BrowserClosed = false },
            "wrong-inventory" => ValidReceipt() with { SurfaceCount = 5 },
            "not-rendered" => ValidReceipt() with { RenderedReady = false },
            "browser-not-suppressed" => ValidReceipt() with { BrowserSuppressed = false },
            "occupied-port" => ValidReceipt() with { PortWasAvailableBeforeStart = false },
            "non-public-boundary" => ValidReceipt() with { PublicHttpBoundaryUsed = false },
            "process-tree-retained" => ValidReceipt() with { ExpoProcessTreeAbsent = false },
            "drain-incomplete" => ValidReceipt() with { ExpoDrainsCompleted = false },
            "inspection-incomplete" => ValidReceipt() with { ExpoInspectionCompleted = false },
            "staged-source-retained" => ValidReceipt() with { StagedSourceRemoved = false },
            "npm-cache-retained" => ValidReceipt() with { NpmCacheRemoved = false },
            "browser-run-retained" => ValidReceipt() with { BrowserRunDirectoryRemoved = false },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        Assert.That(
            () => FinalWebHarnessEvidenceManifest.Create(
                CreateTrx(FinalWebHarnessEvidenceManifest.RequiredWebHarnessTests),
                CreateTrx(FinalWebHarnessEvidenceManifest.RequiredLocatorContractTests),
                receipt),
            Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase("absent-rendered-ready")]
    [TestCase("false-browser-suppressed")]
    [TestCase("unknown-private-field")]
    public void EvidenceReceipt_rejects_absent_false_or_unknown_private_fields(string mutation)
    {
        var receipt = JsonNode.Parse(FinalWebHarnessEvidenceReceiptWriter.Serialize(ValidReceipt()))!.AsObject();
        switch (mutation)
        {
            case "absent-rendered-ready":
                receipt.Remove("renderedReady");
                break;
            case "false-browser-suppressed":
                receipt["browserSuppressed"] = false;
                break;
            case "unknown-private-field":
                receipt["privatePath"] = "raw-private-path-canary";
                break;
        }

        Assert.That(
            () => FinalWebHarnessEvidenceReceiptWriter.ValidateSerializedReceipt(receipt.ToJsonString()),
            Throws.TypeOf<InvalidOperationException>());
    }

    private static FinalWebHarnessEvidenceReceipt ValidReceipt() => new(
        CommitSha: "cd930cce76c030b0ffe631f0bdd79712f97d171f",
        SourceStateMatched: true,
        SourceExported: true,
        NodeInstalled: true,
        ExpoReady: true,
        RenderedReady: true,
        BrowserSuppressed: true,
        PortWasAvailableBeforeStart: true,
        PublicHttpBoundaryUsed: true,
        BrowserClosed: true,
        IsolatedContext: true,
        ExpoProcessTreeAbsent: true,
        ExpoDrainsCompleted: true,
        ExpoInspectionCompleted: true,
        StagedSourceRemoved: true,
        NpmCacheRemoved: true,
        BrowserRunDirectoryRemoved: true,
        SurfaceCount: 6);

    private static string CreateTrx(IReadOnlyList<string> testNames, string outcome = "Passed")
    {
        var results = string.Join(string.Empty, testNames.Select(name =>
            $"<UnitTestResult testName=\"{name}\" outcome=\"{outcome}\" />"));
        var passed = outcome == "Passed" ? testNames.Count : 0;
        var notExecuted = outcome == "NotExecuted" ? testNames.Count : 0;
        return $"<TestRun runUser=\"raw-user-canary\"><ResultSummary><Counters total=\"{testNames.Count}\" executed=\"{testNames.Count - notExecuted}\" passed=\"{passed}\" failed=\"0\" timeout=\"0\" notExecuted=\"{notExecuted}\" /></ResultSummary><Results>{results}</Results><Output>raw-secret-canary</Output></TestRun>";
    }
}
