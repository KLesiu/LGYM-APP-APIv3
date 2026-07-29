using LgymApi.BackgroundWorker.Common.Jobs;
using LgymApi.Resources;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ProjectReferenceGraphGuardTests
{
    private const string CurrentGraphDocumentPath = "docs/modular-monolith/issue-380-project-reference-graph.md";

    private static IReadOnlyList<string> ExpectedProjectNames => ProjectReferenceGraphManifest.ProjectNames;

    private static IReadOnlyList<string> ExpectedEdgeIdentities => ProjectReferenceGraphManifest.EdgeIdentities;

    [Test]
    public void Solution_ProjectReference_Graph_Should_Match_The_Exact_18_Project_90_Edge_Manifest()
    {
        AssertExactGraph(LoadSolutionGraph());
    }

    [Test]
    public void Architecture_Guards_Should_Compile_Against_The_Frozen_Common_And_Resource_Contracts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(IEmailJob).Assembly.GetName().Name, Is.EqualTo("LgymApi.BackgroundWorker.Common"));
            Assert.That(Messages.GenericTrainerDisplayName, Is.Not.Empty);
        });
    }

    [Test]
    public void Rejects_Added_Missing_Duplicate_And_Cyclic_ProjectReference_Edges()
    {
        var addedEdgeFixture = ExpectedGraph() with
        {
            EdgeIdentities = [.. ExpectedEdgeIdentities, "LgymApi.Resources.Generator -> LgymApi.Domain"]
        };
        var missingEdgeFixture = ExpectedGraph() with
        {
            EdgeIdentities = ExpectedEdgeIdentities
                .Where(edge => edge != "LgymApi.Api -> LgymApi.Resources")
                .ToArray()
        };
        var duplicateEdgeFixture = ExpectedGraph() with
        {
            EdgeIdentities = [.. ExpectedEdgeIdentities, "LgymApi.Api -> LgymApi.Application"]
        };
        var cyclicEdgeFixture = ExpectedGraph() with
        {
            EdgeIdentities = [.. ExpectedEdgeIdentities, "LgymApi.Domain -> LgymApi.Application"]
        };

        var addedEdgeException = Assert.Throws<AssertionException>(() => AssertExactGraph(addedEdgeFixture));
        var missingEdgeException = Assert.Throws<AssertionException>(() => AssertExactGraph(missingEdgeFixture));
        var duplicateEdgeException = Assert.Throws<AssertionException>(() => AssertExactGraph(duplicateEdgeFixture));
        var cyclicEdgeException = Assert.Throws<AssertionException>(() => AssertExactGraph(cyclicEdgeFixture));

        Assert.Multiple(() =>
        {
            Assert.That(addedEdgeException!.Message, Does.Contain("Unexpected project-reference edge: LgymApi.Resources.Generator -> LgymApi.Domain"));
            Assert.That(missingEdgeException!.Message, Does.Contain("Missing project-reference edge: LgymApi.Api -> LgymApi.Resources"));
            Assert.That(duplicateEdgeException!.Message, Does.Contain("Duplicate project-reference edge: LgymApi.Api -> LgymApi.Application"));
            Assert.That(cyclicEdgeException!.Message, Does.Contain("Project-reference cycle: LgymApi.Application -> LgymApi.Domain -> LgymApi.Application"));
        });
    }

    [Test]
    public void Nineteenth_Project_Should_Fail_The_Manifest()
    {
        var fixture = ExpectedGraph() with
        {
            ProjectNames = [.. ExpectedProjectNames, "LgymApi.NineteenthProject"]
        };

        var exception = Assert.Throws<AssertionException>(() => AssertExactGraph(fixture));

        Assert.That(exception!.Message, Does.Contain("Expected 18 projects but found 19"));
        Assert.That(exception.Message, Does.Contain("Unexpected project: LgymApi.NineteenthProject"));
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
        var staleMarkdown = "The graph has exactly 18 projects and 89 `ProjectReference` edges.";

        var exception = Assert.Throws<AssertionException>(() => AssertCurrentGraphDocumentation(staleMarkdown));

        Assert.That(exception!.Message, Does.Contain("90 `ProjectReference` edges"));
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
        var guidance = "The current project-reference graph is fixed at exactly 18 projects and 90 edges. " +
            "The authoritative graph is `docs/modular-monolith/issue-380-project-reference-graph.md`.";
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
        var duplicateProjects = FindDuplicates(actualProjects);
        var duplicateEdges = FindDuplicates(actualEdges);
        var cycle = FindCycle(actualEdges);
        var violations = new List<string>();

        if (actualProjects.Length != 18)
        {
            violations.Add($"Expected 18 projects but found {actualProjects.Length}.");
        }

        if (actualEdges.Length != 90)
        {
            violations.Add($"Expected 90 project-reference edges but found {actualEdges.Length}.");
        }

        violations.AddRange(duplicateProjects.Select(project => $"Duplicate project: {project}"));
        violations.AddRange(duplicateEdges.Select(edge => $"Duplicate project-reference edge: {edge}"));
        violations.AddRange(expectedProjects.Except(actualProjects, StringComparer.Ordinal)
            .Select(project => $"Missing project: {project}"));
        violations.AddRange(actualProjects.Except(expectedProjects, StringComparer.Ordinal)
            .Select(project => $"Unexpected project: {project}"));
        violations.AddRange(expectedEdges.Except(actualEdges, StringComparer.Ordinal)
            .Select(edge => $"Missing project-reference edge: {edge}"));
        violations.AddRange(actualEdges.Except(expectedEdges, StringComparer.Ordinal)
            .Select(edge => $"Unexpected project-reference edge: {edge}"));

        if (cycle != null)
        {
            violations.Add($"Project-reference cycle: {cycle}");
        }

        Assert.That(
            violations,
            Is.Empty,
            "Project-reference graph drift detected:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static IReadOnlyList<string> FindDuplicates(IEnumerable<string> values)
    {
        return values
            .GroupBy(value => value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? FindCycle(IEnumerable<string> edgeIdentities)
    {
        var adjacency = edgeIdentities
            .Select(ParseEdge)
            .GroupBy(edge => edge.Source, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.Target).Distinct(StringComparer.Ordinal).OrderBy(target => target, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();
        string? cycle = null;

        foreach (var source in adjacency.Keys.OrderBy(source => source, StringComparer.Ordinal))
        {
            if (!visited.Contains(source) && Visit(source))
            {
                return cycle;
            }
        }

        return null;

        bool Visit(string source)
        {
            visited.Add(source);
            visiting.Add(source);
            path.Add(source);

            foreach (var target in adjacency.GetValueOrDefault(source, []))
            {
                if (visiting.Contains(target))
                {
                    var cycleStart = path.IndexOf(target);
                    cycle = string.Join(" -> ", path.Skip(cycleStart).Append(target));
                    return true;
                }

                if (!visited.Contains(target) && Visit(target))
                {
                    return true;
                }
            }

            visiting.Remove(source);
            path.RemoveAt(path.Count - 1);
            return false;
        }
    }

    private static ProjectReferenceEdge ParseEdge(string edgeIdentity)
    {
        const string separator = " -> ";
        var separatorIndex = edgeIdentity.IndexOf(separator, StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex + separator.Length >= edgeIdentity.Length)
        {
            throw new InvalidOperationException($"Invalid project-reference edge identity '{edgeIdentity}'.");
        }

        return new ProjectReferenceEdge(
            edgeIdentity[..separatorIndex],
            edgeIdentity[(separatorIndex + separator.Length)..]);
    }

    private static void AssertCurrentGraphDocumentation(string markdown)
    {
        Assert.That(markdown, Does.Contain("The graph has exactly 18 projects and 90 `ProjectReference` edges."));
        Assert.That(markdown, Does.Contain("The forbidden cross-project edge complement contains exactly 216 edges."));
        Assert.That(
            markdown,
            Does.Contain("Topological order: " + string.Join(
                " -> ",
                ProjectReferenceGraphManifest.TopologicalOrder.Select(project => $"`{project}`"))));

        Assert.Multiple(() =>
        {
            foreach (var project in ExpectedProjectNames)
            {
                Assert.That(markdown, Does.Contain($"`{project}`"), $"Missing documented project '{project}'.");
            }

            foreach (var edge in ExpectedEdgeIdentities)
            {
                var parsedEdge = ParseEdge(edge);
                Assert.That(
                    markdown,
                    Does.Contain($"- `{parsedEdge.Source}` -> `{parsedEdge.Target}`"),
                    $"Missing documented project-reference edge '{edge}'.");
                Assert.That(
                    markdown,
                    Does.Contain($"| `{edge}` |"),
                    $"Missing documented direct-use evidence for '{edge}'.");
            }
        });
    }

    private static void AssertRootAgentGuidance(byte[] agentBytes, byte[] agentsBytes)
    {
        Assert.That(agentBytes, Is.EqualTo(agentsBytes), "Root agent guidance files must be byte-identical.");

        var guidance = System.Text.Encoding.UTF8.GetString(agentBytes);
        Assert.Multiple(() =>
        {
            Assert.That(guidance, Does.Contain("The current project-reference graph is fixed at exactly 18 projects and 90 edges."));
            Assert.That(guidance, Does.Contain("The authoritative current graph is `docs/modular-monolith/issue-380-project-reference-graph.md`"));
            Assert.That(guidance, Does.Contain("No project-reference edge may be added, removed, duplicated, or made cyclic outside an approved topology change."));
        });
    }

    private sealed record ProjectGraphFixture(
        IReadOnlyList<string> ProjectNames,
        IReadOnlyList<string> EdgeIdentities);

    private sealed record ProjectReferenceEdge(string Source, string Target);
}
