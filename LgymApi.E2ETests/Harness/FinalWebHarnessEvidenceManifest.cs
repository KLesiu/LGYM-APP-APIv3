using System.Text.Json;
using System.Xml.Linq;
using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed record FinalWebHarnessEvidenceReceipt(
    string CommitSha,
    bool SourceStateMatched,
    bool SourceExported,
    bool NodeInstalled,
    bool ExpoReady,
    bool RenderedReady,
    bool BrowserSuppressed,
    bool PortWasAvailableBeforeStart,
    bool PublicHttpBoundaryUsed,
    bool BrowserClosed,
    bool IsolatedContext,
    bool ExpoProcessTreeAbsent,
    bool ExpoDrainsCompleted,
    bool ExpoInspectionCompleted,
    bool StagedSourceRemoved,
    bool NpmCacheRemoved,
    bool BrowserRunDirectoryRemoved,
    int SurfaceCount);

internal static class FinalWebHarnessEvidenceManifest
{
    internal static readonly string[] RequiredWebHarnessTests =
    [
        "Pinned_source_is_exported_started_and_navigated_by_Chromium"
    ];

    internal static readonly string[] RequiredLocatorContractTests =
    [
        "Locator_catalog_covers_exactly_the_six_issue_434_surfaces",
        "Locator_catalog_matches_the_archived_pinned_routes_and_text",
        "DOM_fallbacks_are_centralized_and_no_Unauthorized_body_is_hard_coded"
    ];

    internal static string Create(
        string webHarnessTrx,
        string locatorContractTrx,
        FinalWebHarnessEvidenceReceipt receipt)
    {
        ValidateReceipt(receipt);
        var webHarness = ParseCanonicalTrx(webHarnessTrx, RequiredWebHarnessTests);
        var locatorContract = ParseCanonicalTrx(locatorContractTrx, RequiredLocatorContractTests);
        return JsonSerializer.Serialize(new
        {
            schema = "issue-434-web-evidence-v1",
            source = new { receipt.CommitSha, receipt.SourceStateMatched, receipt.SourceExported },
            webHarness,
            locatorContract,
            receipts = new
            {
                receipt.NodeInstalled,
                receipt.ExpoReady,
                receipt.RenderedReady,
                receipt.BrowserSuppressed,
                receipt.PortWasAvailableBeforeStart,
                receipt.PublicHttpBoundaryUsed,
                receipt.BrowserClosed,
                receipt.IsolatedContext,
                receipt.ExpoProcessTreeAbsent,
                receipt.ExpoDrainsCompleted,
                receipt.ExpoInspectionCompleted,
                receipt.StagedSourceRemoved,
                receipt.NpmCacheRemoved,
                receipt.BrowserRunDirectoryRemoved,
                receipt.SurfaceCount,
                cleanup = new
                {
                    sourceStateMatched = receipt.SourceStateMatched,
                    browserClosed = receipt.BrowserClosed,
                    expoProcessTreeAbsent = receipt.ExpoProcessTreeAbsent,
                    stagedSourceRemoved = receipt.StagedSourceRemoved,
                    npmCacheRemoved = receipt.NpmCacheRemoved,
                    browserRunDirectoryRemoved = receipt.BrowserRunDirectoryRemoved
                }
            }
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    private static object ParseCanonicalTrx(string rawTrx, IReadOnlyList<string> requiredNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTrx);
        var document = XDocument.Parse(rawTrx, LoadOptions.None);
        var counters = document.Descendants().SingleOrDefault(element => element.Name.LocalName == "Counters")
            ?? throw new InvalidOperationException("Web harness TRX counters are missing.");
        var total = ReadCounter(counters, "total");
        var executed = ReadCounter(counters, "executed");
        var passed = ReadCounter(counters, "passed");
        var failed = ReadCounter(counters, "failed");
        var notExecuted = ReadCounter(counters, "notExecuted");
        var tests = document.Descendants()
            .Where(element => element.Name.LocalName == "UnitTestResult")
            .Select(element => new
            {
                Name = (string?)element.Attribute("testName"),
                Outcome = (string?)element.Attribute("outcome")
            })
            .Where(result => result.Name is not null && requiredNames.Contains(result.Name, StringComparer.Ordinal))
            .ToArray();

        if (total == 0 || executed != total || passed != total || failed != 0 || notExecuted != 0 ||
            tests.Length != requiredNames.Count || tests.Any(result => result.Outcome != "Passed") ||
            tests.Select(result => result.Name).Distinct(StringComparer.Ordinal).Count() != requiredNames.Count)
        {
            throw new InvalidOperationException("Web harness TRX evidence is incomplete.");
        }

        return new
        {
            counters = new { total, executed, passed, failed, notExecuted },
            tests = tests.OrderBy(result => result.Name, StringComparer.Ordinal)
                .Select(result => new { name = result.Name, outcome = result.Outcome })
        };
    }

    private static int ReadCounter(XElement counters, string name) => int.Parse(
        (string?)counters.Attribute(name) ?? throw new InvalidOperationException("Web harness TRX counters are incomplete."));

    internal static void ValidateReceipt(FinalWebHarnessEvidenceReceipt receipt)
    {
        if (receipt.CommitSha != E2EOptionsValidator.PinnedCommitSha ||
            !receipt.SourceStateMatched || !receipt.SourceExported || !receipt.NodeInstalled ||
            !receipt.ExpoReady || !receipt.RenderedReady || !receipt.BrowserSuppressed ||
            !receipt.PortWasAvailableBeforeStart || !receipt.PublicHttpBoundaryUsed || !receipt.BrowserClosed ||
            !receipt.IsolatedContext || !receipt.ExpoProcessTreeAbsent || !receipt.ExpoDrainsCompleted ||
            !receipt.ExpoInspectionCompleted || !receipt.StagedSourceRemoved || !receipt.NpmCacheRemoved ||
            !receipt.BrowserRunDirectoryRemoved || receipt.SurfaceCount != 6)
        {
            throw new InvalidOperationException("Web harness evidence receipt is incomplete.");
        }
    }
}
