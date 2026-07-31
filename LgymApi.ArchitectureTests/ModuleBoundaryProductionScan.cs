using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

public static class ModuleBoundaryProductionScan
{
    private static readonly string[] RequiredProjects =
    [
        "LgymApi.Application",
        "LgymApi.Domain",
        "LgymApi.Platform",
        "LgymApi.Identity",
        "LgymApi.TrainingPlanning",
        "LgymApi.Notifications"
    ];

    public static ModuleBoundaryProductionCompilation Prepare()
    {
        AssertExactProjectCoverage(RequiredProjects);
        var (repoRoot, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation(RequiredProjects);
        var sourceTreeCounts = RequiredProjects.ToDictionary(
            project => project,
            project => syntaxTrees.Count(tree => IsProjectSource(tree.FilePath, repoRoot, project)),
            StringComparer.Ordinal);

        var emptyProjects = sourceTreeCounts
            .Where(entry => entry.Value == 0)
            .Select(entry => entry.Key)
            .ToArray();
        var countedSourceTrees = sourceTreeCounts.Values.Sum();
        if (emptyProjects.Length > 0 || countedSourceTrees != syntaxTrees.Count)
        {
            throw new AssertionException(
                $"Module-boundary production scan did not observe every source tree. Empty projects: {FormatValues(emptyProjects)}; " +
                $"counted trees: {countedSourceTrees}; compiled trees: {syntaxTrees.Count}.");
        }

        return new ModuleBoundaryProductionCompilation(repoRoot, compilation, syntaxTrees, sourceTreeCounts);
    }

    public static void AssertExactProjectCoverage(IEnumerable<string> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var proposedProjects = projects.ToArray();
        var missingProjects = RequiredProjects.Except(proposedProjects, StringComparer.Ordinal).ToArray();
        var unexpectedProjects = proposedProjects.Except(RequiredProjects, StringComparer.Ordinal).ToArray();
        var duplicateProjects = proposedProjects
            .GroupBy(project => project, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (proposedProjects.SequenceEqual(RequiredProjects, StringComparer.Ordinal))
        {
            return;
        }

        throw new AssertionException(
            "Module-boundary production scans must compile exactly Application, Domain, Platform, Identity, TrainingPlanning, and Notifications in canonical order. " +
            $"Missing: {FormatValues(missingProjects)}; unexpected: {FormatValues(unexpectedProjects)}; duplicates: {FormatValues(duplicateProjects)}.");
    }

    public static string? ResolveCanonicalModule(SyntaxTree tree, string repoRoot)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var pathModule = ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(tree.FilePath);
        if (pathModule != null)
        {
            return pathModule;
        }

        var relativePath = ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, tree.FilePath));
        var physicalAssemblyModule = relativePath switch
        {
            var path when path.StartsWith("LgymApi.Platform/", StringComparison.OrdinalIgnoreCase)
                => ArchitectureTestHelpers.PlatformModuleName,
            var path when path.StartsWith("LgymApi.Identity/", StringComparison.OrdinalIgnoreCase)
                => ArchitectureTestHelpers.IdentityModuleName,
            var path when path.StartsWith("LgymApi.TrainingPlanning/", StringComparison.OrdinalIgnoreCase)
                => ArchitectureTestHelpers.TrainingPlanningModuleName,
            var path when path.StartsWith("LgymApi.Notifications/", StringComparison.OrdinalIgnoreCase)
                => ArchitectureTestHelpers.NotificationsModuleName,
            _ => null
        };
        if (physicalAssemblyModule != null)
        {
            return physicalAssemblyModule;
        }

        var declaredNamespaces = tree.GetRoot()
            .DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(declaration => declaration.Name.ToString());

        return declaredNamespaces
            .Select(ResolveCanonicalNamespace)
            .FirstOrDefault(module => module != null);
    }

    private static string? ResolveCanonicalNamespace(string namespaceName)
    {
        if (MatchesNamespace(namespaceName, "LgymApi.Application.Identity")
            || MatchesNamespace(namespaceName, "LgymApi.Application.User")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Role")
            || MatchesNamespace(namespaceName, "LgymApi.Application.ExternalAuth")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Features.AdminManagement")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Features.Tutorial")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Features.PasswordReset"))
        {
            return ArchitectureTestHelpers.IdentityModuleName;
        }

        if (MatchesNamespace(namespaceName, "LgymApi.Application.Notifications"))
        {
            return ArchitectureTestHelpers.NotificationsModuleName;
        }

        if (MatchesNamespace(namespaceName, "LgymApi.Application.Reporting")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Features.Reporting"))
        {
            return ArchitectureTestHelpers.ReportingModuleName;
        }

        if (MatchesNamespace(namespaceName, "LgymApi.Application.TrainingPlanning")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Features.PlanDay"))
        {
            return ArchitectureTestHelpers.TrainingPlanningModuleName;
        }

        if (MatchesNamespace(namespaceName, "LgymApi.Application.WorkoutProgress")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Training")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Exercise")
            || MatchesNamespace(namespaceName, "LgymApi.Application.ExerciseScores")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Gym")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Measurements")
            || MatchesNamespace(namespaceName, "LgymApi.Application.EloRegistry")
            || MatchesNamespace(namespaceName, "LgymApi.Application.MainRecords"))
        {
            return ArchitectureTestHelpers.WorkoutProgressModuleName;
        }

        if (MatchesNamespace(namespaceName, "LgymApi.Application.Coaching")
            || MatchesNamespace(namespaceName, "LgymApi.Application.TrainerRelationships")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Features.TraineeNotes"))
        {
            return ArchitectureTestHelpers.CoachingModuleName;
        }

        if (MatchesNamespace(namespaceName, "LgymApi.Application.Nutrition")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Features.DietPlans")
            || MatchesNamespace(namespaceName, "LgymApi.Application.Features.Supplementation"))
        {
            return ArchitectureTestHelpers.NutritionModuleName;
        }

        return null;
    }

    private static bool IsProjectSource(string filePath, string repoRoot, string project)
    {
        var relativePath = ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, filePath));
        return relativePath.StartsWith($"{project}/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesNamespace(string namespaceName, string expectedPrefix)
        => namespaceName.Equals(expectedPrefix, StringComparison.Ordinal)
            || namespaceName.StartsWith($"{expectedPrefix}.", StringComparison.Ordinal);

    private static string FormatValues(IEnumerable<string> values)
    {
        var materialized = values.ToArray();
        return materialized.Length == 0 ? "none" : string.Join(", ", materialized);
    }
}

public sealed record ModuleBoundaryProductionCompilation(
    string RepoRoot,
    CSharpCompilation Compilation,
    IReadOnlyList<SyntaxTree> SyntaxTrees,
    IReadOnlyDictionary<string, int> SourceTreeCounts)
{
    public string DescribeSourceTreeCounts()
        => string.Join(", ", SourceTreeCounts.Select(entry => $"{entry.Key}={entry.Value}"));
}
