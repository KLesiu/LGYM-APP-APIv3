using System.Text.Json;

namespace LgymApi.E2ETests.Harness;

internal static class FinalWebHarnessEvidenceReceiptWriter
{
    internal const string FileName = "issue-434-web-harness.receipt.json";

    internal static void Write(FinalWebHarnessEvidenceReceipt receipt)
    {
        var repositoryRoot = RepositoryRoot.Find();
        var outputPath = Path.Combine(repositoryRoot, "LgymApi.E2ETests", "TestResults", FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, Serialize(receipt));
    }

    internal static string Serialize(FinalWebHarnessEvidenceReceipt receipt)
    {
        FinalWebHarnessEvidenceManifest.ValidateReceipt(receipt);
        var serialized = JsonSerializer.Serialize(receipt, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        ValidateSerializedReceipt(serialized);
        return serialized;
    }

    internal static void ValidateSerializedReceipt(string serialized)
    {
        using var document = JsonDocument.Parse(serialized);
        var expectedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "commitSha", "sourceStateMatched", "sourceExported", "nodeInstalled", "expoReady",
            "renderedReady", "browserSuppressed", "portWasAvailableBeforeStart", "publicHttpBoundaryUsed",
            "browserClosed", "isolatedContext", "expoProcessTreeAbsent", "expoDrainsCompleted",
            "expoInspectionCompleted", "stagedSourceRemoved", "npmCacheRemoved", "browserRunDirectoryRemoved",
            "surfaceCount"
        };
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.EnumerateObject().All(property => expectedNames.Contains(property.Name)) ||
            document.RootElement.EnumerateObject().Count() != expectedNames.Count ||
            !expectedNames.All(name => document.RootElement.TryGetProperty(name, out _)) ||
            document.RootElement.GetProperty("commitSha").ValueKind != JsonValueKind.String ||
            document.RootElement.GetProperty("surfaceCount").ValueKind != JsonValueKind.Number ||
            expectedNames.Where(name => name is not "commitSha" and not "surfaceCount")
                .Any(name => document.RootElement.GetProperty(name).ValueKind != JsonValueKind.True))
        {
            throw new InvalidOperationException("Web harness evidence receipt is incomplete.");
        }
    }
}
