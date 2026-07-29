namespace LgymApi.ArchitectureTests;

internal static class ProjectReferenceImportGuard
{
    public static ProjectImportAnalysis AnalyzeSolution(string repositoryRoot)
    {
        return AnalyzeFixture(ProjectReferenceSourceScanner.Scan(repositoryRoot));
    }

    public static ProjectImportAnalysis AnalyzeFixture(ProjectImportFixture fixture)
    {
        var duplicateEdges = fixture.EdgeIdentities
            .GroupBy(edge => edge, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(edge => edge, StringComparer.Ordinal)
            .ToArray();
        var uniqueEdges = fixture.EdgeIdentities.ToHashSet(StringComparer.Ordinal);
        var semanticEvidence = fixture.SymbolUses
            .GroupBy(use => use.EdgeIdentity, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(use => use.FilePath, StringComparer.Ordinal)
                    .ThenBy(use => use.Line)
                    .First(),
                StringComparer.Ordinal);
        var analyzerEdges = fixture.AnalyzerEdgeIdentities.ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();

        violations.AddRange(duplicateEdges.Select(edge => $"Duplicate project-reference edge: {edge}"));
        violations.AddRange(uniqueEdges
            .Intersect(fixture.ForbiddenEdgeIdentities, StringComparer.Ordinal)
            .OrderBy(edge => edge, StringComparer.Ordinal)
            .Select(edge => $"Forbidden project-reference edge: {edge}"));

        var cycle = FindCycle(uniqueEdges);
        if (cycle is not null)
        {
            violations.Add($"Project-reference cycle: {cycle}");
        }

        foreach (var edge in uniqueEdges.OrderBy(edge => edge, StringComparer.Ordinal))
        {
            if (!semanticEvidence.ContainsKey(edge) && !analyzerEdges.Contains(edge))
            {
                violations.Add($"Unused project-reference edge: {edge}");
            }
        }

        foreach (var use in semanticEvidence.Values.OrderBy(use => use.EdgeIdentity, StringComparer.Ordinal))
        {
            if (uniqueEdges.Contains(use.EdgeIdentity))
            {
                continue;
            }

            var path = FindPath(use.SourceProject, use.TargetProject, uniqueEdges);
            violations.Add(path is null
                ? $"Missing project-reference edge required by source import: {use.EdgeIdentity}"
                : $"Transitive project-reference reliance: {use.EdgeIdentity} via {string.Join(" -> ", path)}");
        }

        foreach (var analyzerEdge in analyzerEdges.OrderBy(edge => edge, StringComparer.Ordinal))
        {
            if (!uniqueEdges.Contains(analyzerEdge))
            {
                violations.Add($"Missing analyzer project-reference edge: {analyzerEdge}");
            }
        }

        var topologicalOrder = cycle is null
            ? TopologicalSort(fixture.ProjectNames, uniqueEdges)
            : [];
        if (cycle is null && !topologicalOrder.SequenceEqual(fixture.ExpectedTopologicalOrder, StringComparer.Ordinal))
        {
            violations.Add(
                $"Topological order drift: expected {string.Join(" -> ", fixture.ExpectedTopologicalOrder)}; "
                + $"actual {string.Join(" -> ", topologicalOrder)}");
        }

        return new ProjectImportAnalysis(
            violations,
            semanticEvidence,
            analyzerEdges,
            topologicalOrder);
    }

    private static IReadOnlyList<string> TopologicalSort(
        IReadOnlyList<string> projectNames,
        IReadOnlySet<string> edgeIdentities)
    {
        var edges = edgeIdentities.Select(ParseEdge).ToArray();
        var dependencyCounts = projectNames.ToDictionary(
            project => project,
            project => edges.Count(edge => string.Equals(edge.Source, project, StringComparison.Ordinal)),
            StringComparer.Ordinal);
        var dependents = edges
            .GroupBy(edge => edge.Target, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.Source).OrderBy(source => source, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var ready = new SortedSet<string>(
            dependencyCounts.Where(pair => pair.Value == 0).Select(pair => pair.Key),
            StringComparer.Ordinal);
        var order = new List<string>();

        while (ready.Count > 0)
        {
            var project = ready.Min!;
            ready.Remove(project);
            order.Add(project);

            foreach (var dependent in dependents.GetValueOrDefault(project, []))
            {
                dependencyCounts[dependent]--;
                if (dependencyCounts[dependent] == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        return order;
    }

    private static string? FindCycle(IReadOnlySet<string> edgeIdentities)
    {
        var adjacency = edgeIdentities
            .Select(ParseEdge)
            .GroupBy(edge => edge.Source, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.Target).OrderBy(target => target, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var source in adjacency.Keys.OrderBy(source => source, StringComparer.Ordinal))
        {
            var cycle = Visit(source);
            if (cycle is not null)
            {
                return cycle;
            }
        }

        return null;

        string? Visit(string source)
        {
            if (visited.Contains(source))
            {
                return null;
            }

            visited.Add(source);
            visiting.Add(source);
            path.Add(source);

            foreach (var target in adjacency.GetValueOrDefault(source, []))
            {
                if (visiting.Contains(target))
                {
                    return string.Join(" -> ", path.Skip(path.IndexOf(target)).Append(target));
                }

                var nestedCycle = Visit(target);
                if (nestedCycle is not null)
                {
                    return nestedCycle;
                }
            }

            visiting.Remove(source);
            path.RemoveAt(path.Count - 1);
            return null;
        }
    }

    private static IReadOnlyList<string>? FindPath(
        string source,
        string target,
        IReadOnlySet<string> edgeIdentities)
    {
        var adjacency = edgeIdentities
            .Select(ParseEdge)
            .GroupBy(edge => edge.Source, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.Target).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var queue = new Queue<IReadOnlyList<string>>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { source };
        queue.Enqueue([source]);

        while (queue.TryDequeue(out var path))
        {
            foreach (var next in adjacency.GetValueOrDefault(path[^1], []))
            {
                var nextPath = path.Append(next).ToArray();
                if (string.Equals(next, target, StringComparison.Ordinal))
                {
                    return nextPath;
                }

                if (visited.Add(next))
                {
                    queue.Enqueue(nextPath);
                }
            }
        }

        return null;
    }

    private static ProjectImportEdge ParseEdge(string identity)
    {
        var parts = identity.Split(" -> ", StringSplitOptions.None);
        return new ProjectImportEdge(parts[0], parts[1]);
    }
}

internal sealed record ProjectImportFixture(
    IReadOnlyList<string> ProjectNames,
    IReadOnlyList<string> EdgeIdentities,
    IReadOnlyList<ProjectImportUse> SymbolUses,
    IReadOnlyList<string> AnalyzerEdgeIdentities,
    IReadOnlyList<string> ForbiddenEdgeIdentities,
    IReadOnlyList<string> ExpectedTopologicalOrder);

internal sealed record ProjectImportUse(
    string SourceProject,
    string TargetProject,
    string FilePath,
    int Line,
    string Symbol)
{
    public string EdgeIdentity => $"{SourceProject} -> {TargetProject}";
}

internal sealed record ProjectImportAnalysis(
    IReadOnlyList<string> Violations,
    IReadOnlyDictionary<string, ProjectImportUse> SemanticEvidenceByEdge,
    IReadOnlySet<string> AnalyzerEdgeIdentities,
    IReadOnlyList<string> TopologicalOrder);

internal sealed record ProjectImportEdge(string Source, string Target);
