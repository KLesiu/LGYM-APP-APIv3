namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ProjectReferenceGraphGuardTests
{
    private const string CurrentGraphDocumentPath = "docs/modular-monolith/issue-380-project-reference-graph.md";

    private static readonly string[] ExpectedProjectNames =
    [
        "LgymApi.Api",
        "LgymApi.Application",
        "LgymApi.ArchitectureTests",
        "LgymApi.BackgroundWorker",
        "LgymApi.BackgroundWorker.Common",
        "LgymApi.DataSeeder",
        "LgymApi.DataSeeder.Tests",
        "LgymApi.Domain",
        "LgymApi.Infrastructure",
        "LgymApi.IntegrationTests",
        "LgymApi.Resources",
        "LgymApi.Resources.Generator",
        "LgymApi.TestUtils",
        "LgymApi.UnitTests"
    ];

    private static readonly string[] ExpectedEdgeIdentities =
    [
        "LgymApi.Api -> LgymApi.Application",
        "LgymApi.Api -> LgymApi.BackgroundWorker",
        "LgymApi.Api -> LgymApi.Domain",
        "LgymApi.Api -> LgymApi.Infrastructure",
        "LgymApi.Api -> LgymApi.Resources",
        "LgymApi.Application -> LgymApi.Domain",
        "LgymApi.Application -> LgymApi.Resources",
        "LgymApi.ArchitectureTests -> LgymApi.Api",
        "LgymApi.ArchitectureTests -> LgymApi.Application",
        "LgymApi.BackgroundWorker -> LgymApi.Application",
        "LgymApi.BackgroundWorker -> LgymApi.BackgroundWorker.Common",
        "LgymApi.BackgroundWorker -> LgymApi.Infrastructure",
        "LgymApi.BackgroundWorker.Common -> LgymApi.Domain",
        "LgymApi.DataSeeder -> LgymApi.Infrastructure",
        "LgymApi.DataSeeder.Tests -> LgymApi.DataSeeder",
        "LgymApi.DataSeeder.Tests -> LgymApi.Infrastructure",
        "LgymApi.Infrastructure -> LgymApi.Application",
        "LgymApi.Infrastructure -> LgymApi.BackgroundWorker.Common",
        "LgymApi.Infrastructure -> LgymApi.Domain",
        "LgymApi.IntegrationTests -> LgymApi.Api",
        "LgymApi.IntegrationTests -> LgymApi.Infrastructure",
        "LgymApi.IntegrationTests -> LgymApi.TestUtils",
        "LgymApi.Resources -> LgymApi.Resources.Generator",
        "LgymApi.TestUtils -> LgymApi.Application",
        "LgymApi.TestUtils -> LgymApi.BackgroundWorker",
        "LgymApi.TestUtils -> LgymApi.BackgroundWorker.Common",
        "LgymApi.TestUtils -> LgymApi.Domain",
        "LgymApi.TestUtils -> LgymApi.Infrastructure",
        "LgymApi.UnitTests -> LgymApi.Api",
        "LgymApi.UnitTests -> LgymApi.Application",
        "LgymApi.UnitTests -> LgymApi.Infrastructure",
        "LgymApi.UnitTests -> LgymApi.TestUtils"
    ];

    [Test]
    public void Solution_ProjectReference_Graph_Should_Match_The_Exact_14_Project_32_Edge_Manifest()
    {
        AssertExactGraph(LoadSolutionGraph());
    }

    [Test]
    public void Restored_Domain_To_Resources_Edge_Should_Fail_The_Manifest()
    {
        var fixture = ExpectedGraph() with
        {
            EdgeIdentities = [.. ExpectedEdgeIdentities, "LgymApi.Domain -> LgymApi.Resources"]
        };

        var exception = Assert.Throws<AssertionException>(() => AssertExactGraph(fixture));

        Assert.That(exception!.Message, Does.Contain("Unexpected project-reference edge: LgymApi.Domain -> LgymApi.Resources"));
    }

    [Test]
    public void Added_Or_Removed_ProjectReference_Edge_Should_Fail_The_Manifest()
    {
        var addedEdgeFixture = ExpectedGraph() with
        {
            EdgeIdentities = [.. ExpectedEdgeIdentities, "LgymApi.Resources.Generator -> LgymApi.Domain"]
        };
        var removedEdgeFixture = ExpectedGraph() with
        {
            EdgeIdentities = ExpectedEdgeIdentities
                .Where(edge => edge != "LgymApi.Api -> LgymApi.Resources")
                .ToArray()
        };

        var addedEdgeException = Assert.Throws<AssertionException>(() => AssertExactGraph(addedEdgeFixture));
        var removedEdgeException = Assert.Throws<AssertionException>(() => AssertExactGraph(removedEdgeFixture));

        Assert.Multiple(() =>
        {
            Assert.That(addedEdgeException!.Message, Does.Contain("Unexpected project-reference edge: LgymApi.Resources.Generator -> LgymApi.Domain"));
            Assert.That(removedEdgeException!.Message, Does.Contain("Missing project-reference edge: LgymApi.Api -> LgymApi.Resources"));
        });
    }

    [Test]
    public void Fifteenth_Project_Should_Fail_The_Manifest()
    {
        var fixture = ExpectedGraph() with
        {
            ProjectNames = [.. ExpectedProjectNames, "LgymApi.FifteenthProject"]
        };

        var exception = Assert.Throws<AssertionException>(() => AssertExactGraph(fixture));

        Assert.That(exception!.Message, Does.Contain("Expected 14 projects but found 15"));
        Assert.That(exception.Message, Does.Contain("Unexpected project: LgymApi.FifteenthProject"));
    }

    [Test]
    public void Current_ProjectReference_Documentation_Should_Describe_The_Exact_Manifest()
    {
        var markdown = File.ReadAllText(Path.Combine(
            ArchitectureTestHelpers.ResolveRepositoryRoot(),
            CurrentGraphDocumentPath));

        AssertCurrentGraphDocumentation(markdown);
    }

    [Test]
    public void Stale_ProjectReference_Documentation_Should_Fail_The_Guard()
    {
        var staleMarkdown = "The graph has exactly 14 projects and 33 `ProjectReference` edges.";

        var exception = Assert.Throws<AssertionException>(() => AssertCurrentGraphDocumentation(staleMarkdown));

        Assert.That(exception!.Message, Does.Contain("32 `ProjectReference` edges"));
    }

    [Test]
    public void Root_Agent_Guidance_Files_Should_Be_Byte_Identical_And_Describe_The_Current_Graph()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var agentBytes = File.ReadAllBytes(Path.Combine(repositoryRoot, "AGENT.md"));
        var agentsBytes = File.ReadAllBytes(Path.Combine(repositoryRoot, "AGENTS.md"));

        AssertRootAgentGuidance(agentBytes, agentsBytes);
    }

    [Test]
    public void Mismatched_Root_Agent_Guidance_Should_Fail_The_Guard()
    {
        var guidance = "The current project-reference graph is fixed at exactly 14 projects and 32 edges. " +
            "The sole approved current edge delta is removal of `LgymApi.Domain -> LgymApi.Resources`. " +
            "Domain is localization-neutral and must not reference `LgymApi.Resources`.";
        var agentBytes = System.Text.Encoding.UTF8.GetBytes(guidance);
        var agentsBytes = System.Text.Encoding.UTF8.GetBytes(guidance + "\n");

        var exception = Assert.Throws<AssertionException>(() => AssertRootAgentGuidance(agentBytes, agentsBytes));

        Assert.That(exception!.Message, Does.Contain("Root agent guidance files must be byte-identical"));
    }

    private static ProjectGraphFixture LoadSolutionGraph()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var solutionPath = Path.Combine(repositoryRoot, "LgymApi.sln");
        var projectPaths = File
            .ReadLines(solutionPath)
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal))
            .Select(line => line.Split('"'))
            .Where(parts => parts.Length > 5 && parts[5].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(parts => Path.GetFullPath(Path.Combine(repositoryRoot, parts[5])))
            .ToArray();

        return new ProjectGraphFixture(
            projectPaths.Select(path => Path.GetFileNameWithoutExtension(path)!).ToArray(),
            projectPaths
                .SelectMany(ArchitectureTestHelpers.ParseProjectReferences)
                .Select(edge => $"{edge.SourceProject} -> {edge.TargetProject}")
                .ToArray());
    }

    private static ProjectGraphFixture ExpectedGraph()
    {
        return new ProjectGraphFixture(ExpectedProjectNames, ExpectedEdgeIdentities);
    }

    private static void AssertExactGraph(ProjectGraphFixture graph)
    {
        var actualProjects = graph.ProjectNames.OrderBy(project => project, StringComparer.Ordinal).ToArray();
        var actualEdges = graph.EdgeIdentities.OrderBy(edge => edge, StringComparer.Ordinal).ToArray();
        var expectedProjects = ExpectedProjectNames.OrderBy(project => project, StringComparer.Ordinal).ToArray();
        var expectedEdges = ExpectedEdgeIdentities.OrderBy(edge => edge, StringComparer.Ordinal).ToArray();
        var violations = new List<string>();

        if (actualProjects.Length != 14)
        {
            violations.Add($"Expected 14 projects but found {actualProjects.Length}.");
        }

        if (actualEdges.Length != 32)
        {
            violations.Add($"Expected 32 project-reference edges but found {actualEdges.Length}.");
        }

        violations.AddRange(expectedProjects.Except(actualProjects, StringComparer.Ordinal)
            .Select(project => $"Missing project: {project}"));
        violations.AddRange(actualProjects.Except(expectedProjects, StringComparer.Ordinal)
            .Select(project => $"Unexpected project: {project}"));
        violations.AddRange(expectedEdges.Except(actualEdges, StringComparer.Ordinal)
            .Select(edge => $"Missing project-reference edge: {edge}"));
        violations.AddRange(actualEdges.Except(expectedEdges, StringComparer.Ordinal)
            .Select(edge => $"Unexpected project-reference edge: {edge}"));

        Assert.That(
            violations,
            Is.Empty,
            "Project-reference graph drift detected:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void AssertCurrentGraphDocumentation(string markdown)
    {
        Assert.That(markdown, Does.Contain("The graph has exactly 14 projects and 32 `ProjectReference` edges."));
        Assert.That(markdown, Does.Contain("The sole current delta from the immediately preceding 33-edge manifest is:"));
        Assert.That(markdown, Does.Contain("- `LgymApi.Domain` -> `LgymApi.Resources`"));
        Assert.That(markdown, Does.Contain("`LgymApi.Domain` is localization-neutral and has no project reference to `LgymApi.Resources`."));
    }

    private static void AssertRootAgentGuidance(byte[] agentBytes, byte[] agentsBytes)
    {
        Assert.That(agentBytes, Is.EqualTo(agentsBytes), "Root agent guidance files must be byte-identical.");

        var guidance = System.Text.Encoding.UTF8.GetString(agentBytes);
        Assert.Multiple(() =>
        {
            Assert.That(guidance, Does.Contain("The current project-reference graph is fixed at exactly 14 projects and 32 edges."));
            Assert.That(guidance, Does.Contain("The sole approved current edge delta is removal of `LgymApi.Domain -> LgymApi.Resources`"));
            Assert.That(guidance, Does.Contain("Domain is localization-neutral and must not reference `LgymApi.Resources`."));
        });
    }

    private sealed record ProjectGraphFixture(
        IReadOnlyList<string> ProjectNames,
        IReadOnlyList<string> EdgeIdentities);
}
