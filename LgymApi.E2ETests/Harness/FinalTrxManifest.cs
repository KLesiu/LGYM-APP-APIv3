using System.Text.Json;
using System.Xml.Linq;

namespace LgymApi.E2ETests.Harness;

internal sealed record FinalTrxCounters(
    int Total,
    int Executed,
    int Passed,
    int Failed,
    int Timeout,
    int NotExecuted);

internal sealed record FinalTrxTestOutcome(string TestName, string Outcome);

internal sealed record FinalTrxManifest(
    FinalTrxCounters Counters,
    IReadOnlyList<FinalTrxTestOutcome> Tests);

internal static class FinalTrxManifestSerializer
{
    internal static void Write(string rawTrxPath, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTrxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var repositoryRoot = Path.GetFullPath(RepositoryRoot.Find());
        var testResultsRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "LgymApi.E2ETests", "TestResults"));
        var fullOutputPath = Path.GetFullPath(outputPath);
        var relativeOutputPath = Path.GetRelativePath(testResultsRoot, fullOutputPath);
        if (Path.IsPathRooted(relativeOutputPath) || relativeOutputPath == ".." || relativeOutputPath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException("Final TRX manifest output must be under the E2E TestResults directory.", nameof(outputPath));
        }

        var manifest = Parse(File.ReadAllText(rawTrxPath));
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        File.WriteAllText(fullOutputPath, Serialize(manifest));
    }

    internal static FinalTrxManifest Parse(string rawTrx)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawTrx);

        var document = XDocument.Parse(rawTrx, LoadOptions.None);
        var counters = document.Descendants().Single(element => element.Name.LocalName == "Counters");
        var tests = document.Descendants()
            .Where(element => element.Name.LocalName == "UnitTestResult")
            .Select(element => new FinalTrxTestOutcome(
                ReadAttribute(element, "testName"),
                ReadAttribute(element, "outcome")))
            .ToArray();

        return new FinalTrxManifest(
            new FinalTrxCounters(
                ReadCounter(counters, "total"),
                ReadCounter(counters, "executed"),
                ReadCounter(counters, "passed"),
                ReadCounter(counters, "failed"),
                ReadCounter(counters, "timeout"),
                ReadCounter(counters, "notExecuted")),
            tests);
    }

    internal static string Serialize(FinalTrxManifest manifest) =>
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

    private static int ReadCounter(XElement counters, string name) =>
        int.Parse(ReadAttribute(counters, name));

    private static string ReadAttribute(XElement element, string name) =>
        (string?)element.Attribute(name)
        ?? throw new InvalidOperationException($"Final TRX attribute '{name}' is missing.");
}
