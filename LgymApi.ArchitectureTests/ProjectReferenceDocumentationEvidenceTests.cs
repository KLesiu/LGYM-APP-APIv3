using System.Diagnostics;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ProjectReferenceDocumentationEvidenceTests
{
    private const string CurrentGraphDocumentPath = "docs/modular-monolith/issue-380-project-reference-graph.md";
    private static readonly string[] HistoricalIssue375Paths =
    [
        "docs/modular-monolith/issue-375-project-reference-graph.md",
        "docs/modular-monolith/issue-375-architecture-baseline.md"
    ];

    [Test]
    public void Current_Graph_Documentation_Should_Resolve_All_Physical_ProjectReferences()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var markdown = File.ReadAllText(Path.Combine(repositoryRoot, CurrentGraphDocumentPath));
        var evidenceRows = ProjectReferenceDocumentationEvidence.Parse(markdown);
        var scannedImports = ProjectReferenceSourceScanner.Scan(repositoryRoot);
        var analysis = ProjectReferenceImportGuard.AnalyzeFixture(scannedImports);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.StartWith("# Issue #380: Approved Current Project-Reference Graph"));
            Assert.That(markdown, Does.Contain("after the completed issue #387 extraction"));
            Assert.That(scannedImports.ProjectNames, Is.EquivalentTo(ProjectReferenceGraphManifest.ProjectNames));
            Assert.That(scannedImports.ProjectNames, Has.Count.EqualTo(18));
            Assert.That(scannedImports.EdgeIdentities, Has.Count.EqualTo(90));
            Assert.That(scannedImports.EdgeIdentities.Distinct(StringComparer.Ordinal).ToArray(), Has.Length.EqualTo(90));
            Assert.That(ProjectReferenceGraphManifest.ForbiddenEdgeIdentities, Has.Count.EqualTo(216));
            Assert.That(analysis.Violations, Is.Empty, string.Join(Environment.NewLine, analysis.Violations));
            Assert.That(analysis.SemanticEvidenceByEdge, Has.Count.EqualTo(89));
            Assert.That(analysis.AnalyzerEdgeIdentities, Is.EquivalentTo(new[]
            {
                "LgymApi.Resources -> LgymApi.Resources.Generator"
            }));
            Assert.That(analysis.TopologicalOrder, Is.EqualTo(ProjectReferenceGraphManifest.TopologicalOrder));
        });

        Assert.DoesNotThrow(() => ProjectReferenceDocumentationEvidence.AssertMatchesScannedImports(evidenceRows, scannedImports));
        Assert.DoesNotThrow(() => ProjectReferenceDocumentationEvidence.AssertAnalyzerLocators(repositoryRoot, evidenceRows));
    }

    [Test]
    public void Documentation_Parser_Should_Reject_Stale_Wrong_Duplicate_Malformed_And_Added_Evidence_Rows()
    {
        var validFixture = CreateFixture();
        var staleLocator = CreateEvidenceMarkdown("| `A -> B` | `A/Consumer.cs:8` |");
        var wrongTarget = CreateEvidenceMarkdown("| `A -> C` | `A/Consumer.cs:7` |");
        var duplicate = CreateEvidenceMarkdown("| `A -> B` | `A/Consumer.cs:7` |", "| `A -> B` | `A/Other.cs:4` |");
        var malformed = CreateEvidenceMarkdown("| `A -> B` | `A/Consumer.cs:7`");
        var added = CreateEvidenceMarkdown("| `A -> B` | `A/Consumer.cs:7` |", "| `B -> C` | `B/Consumer.cs:4` |");

        Assert.Multiple(() =>
        {
            Assert.That(
                () => ProjectReferenceDocumentationEvidence.AssertMatchesScannedImports(
                    ProjectReferenceDocumentationEvidence.Parse(staleLocator),
                    validFixture),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("locator does not resolve"));
            Assert.That(
                () => ProjectReferenceDocumentationEvidence.AssertMatchesScannedImports(
                    ProjectReferenceDocumentationEvidence.Parse(wrongTarget),
                    CreateFixture(edge: "A -> C")),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("does not resolve to 'C'"));
            Assert.That(
                () => ProjectReferenceDocumentationEvidence.Parse(duplicate),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("Duplicate project-reference evidence rows"));
            Assert.That(
                () => ProjectReferenceDocumentationEvidence.Parse(malformed),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("Malformed project-reference evidence row"));
            Assert.That(
                () => ProjectReferenceDocumentationEvidence.AssertMatchesScannedImports(
                    ProjectReferenceDocumentationEvidence.Parse(added),
                    validFixture),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("Expected 1 project-reference evidence rows but found 2"));
        });
    }

    [Test]
    public void Documentation_Parser_Should_Require_The_Resources_Edge_To_Remain_Analyzer_Evidence()
    {
        var fixture = CreateFixture(edge: "Resources -> Generator", analyzerEdge: "Resources -> Generator");
        var semanticEvidence = CreateEvidenceMarkdown("| `Resources -> Generator` | `Resources/Consumer.cs:3` |");
        var analyzerEvidence = CreateEvidenceMarkdown("| `Resources -> Generator` | `Resources/Resources.csproj:16` analyzer reference |");

        Assert.Multiple(() =>
        {
            Assert.That(
                () => ProjectReferenceDocumentationEvidence.AssertMatchesScannedImports(
                    ProjectReferenceDocumentationEvidence.Parse(semanticEvidence),
                    fixture),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("evidence kind is incorrect"));
            Assert.That(
                () => ProjectReferenceDocumentationEvidence.AssertMatchesScannedImports(
                    ProjectReferenceDocumentationEvidence.Parse(analyzerEvidence),
                    fixture),
                Throws.Nothing);
        });
    }

    [Test]
    public void Historical_Issue375_Documents_Should_Be_Byte_Identical_To_Head()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var changedPaths = HistoricalIssue375Paths
            .Where(path => !IsUnchangedFromHead(repositoryRoot, path))
            .ToArray();

        Assert.That(changedPaths, Is.Empty, "Historical issue-375 documents changed:" + Environment.NewLine + string.Join(Environment.NewLine, changedPaths));
    }

    private static ProjectImportFixture CreateFixture(string edge = "A -> B", string? analyzerEdge = null)
    {
        return new ProjectImportFixture(
            ProjectNames: ["A", "B", "C"],
            EdgeIdentities: [edge],
            SymbolUses: [new ProjectImportUse("A", "B", "A/Consumer.cs", 7, "B.Contract")],
            AnalyzerEdgeIdentities: analyzerEdge is null ? [] : [analyzerEdge],
            ForbiddenEdgeIdentities: [],
            ExpectedTopologicalOrder: []);
    }

    private static string CreateEvidenceMarkdown(params string[] rows)
    {
        return string.Join('\n',
        [
            "## Direct-use evidence",
            "",
            "| Edge | Roslyn-resolved source or analyzer evidence |",
            "| --- | --- |",
            .. rows,
            "",
            "## Locked topology"
        ]);
    }

    private static bool IsUnchangedFromHead(string repositoryRoot, string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("diff");
        startInfo.ArgumentList.Add("--quiet");
        startInfo.ArgumentList.Add("HEAD");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(path);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"git diff did not finish for '{path}'.");
        }

        if (process.ExitCode is not 0 and not 1)
        {
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }

        return process.ExitCode == 0;
    }
}
