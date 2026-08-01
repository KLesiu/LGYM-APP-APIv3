using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ReportingProgressCompatibilityAdapterGuardTests
{
    [Test]
    public void Reporting_Feature_Files_Should_Not_Depend_On_Workout_Progress_Persistence_Or_The_Legacy_Adapter()
    {
        var (repoRoot, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application");
        var reportingDirectory = Path.Combine(repoRoot, "LgymApi.Application", "Features", "Reporting");
        var reportingSourceFiles = EnumerateReportingSourceFiles(reportingDirectory);
        var reportingSourcePaths = reportingSourceFiles
            .Select(NormalizeFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reportingSyntaxTrees = syntaxTrees
            .Where(tree => reportingSourcePaths.Contains(NormalizeFullPath(tree.FilePath)))
            .OrderBy(tree => NormalizeFullPath(tree.FilePath), StringComparer.Ordinal)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(reportingSourceFiles, Is.Not.Empty);
            Assert.That(
                reportingSourceFiles.Select(Path.GetFileName),
                Has.None.EqualTo("IReportSubmissionMeasurementWriter.cs"));
            Assert.That(
                reportingSourceFiles.Select(Path.GetFileName),
                Has.None.EqualTo("ReportSubmissionMeasurementWriter.cs"));
            Assert.That(
                reportingSyntaxTrees.Select(tree => NormalizeFullPath(tree.FilePath)),
                Is.EqualTo(reportingSourceFiles.Select(NormalizeFullPath)));
        });

        var violations = CollectViolations(compilation, reportingSyntaxTrees, repoRoot);

        Assert.That(violations, Is.Empty, BuildViolationMessage(violations));
    }

    [Test]
    public void New_Reporting_Measurement_Repository_Or_Entity_Dependency_Should_Be_Rejected()
    {
        var (repoRoot, compilation, _) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application");
        var fixture = CSharpSyntaxTree.ParseText("""
            using LgymApi.Application.Repositories;
            using MeasurementEntity = LgymApi.Domain.Entities.Measurement;

            namespace LgymApi.Application.Features.Reporting;

            public sealed class NewReportingMeasurementPersistenceDependency
            {
                public IMeasurementRepository MeasurementRepository { get; init; } = default!;
                public MeasurementEntity Measurement { get; init; } = default!;
            }
            """, path: Path.Combine(repoRoot, "LgymApi.Application", "Features", "Reporting", "NewReportingMeasurementPersistenceDependency.cs"));
        var violations = CollectViolations(compilation.AddSyntaxTrees(fixture), [fixture], repoRoot);

        Assert.That(
            violations.Select(violation => violation.TargetMetadataName),
            Is.EquivalentTo(
            [
                "LgymApi.Application.Repositories.IMeasurementRepository",
                "LgymApi.Domain.Entities.Measurement"
            ]),
            BuildViolationMessage(violations));
    }

    private static IReadOnlyList<string> EnumerateReportingSourceFiles(string reportingDirectory)
    {
        return Directory
            .EnumerateFiles(reportingDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !ArchitectureTestHelpers.IsInBuildArtifacts(path))
            .OrderBy(NormalizeFullPath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<Violation> CollectViolations(
        CSharpCompilation compilation,
        IEnumerable<SyntaxTree> syntaxTrees,
        string repoRoot)
    {
        var violations = new Dictionary<string, Violation>(StringComparer.Ordinal);

        foreach (var syntaxTree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: true);
            var root = syntaxTree.GetCompilationUnitRoot();
            foreach (var referencedType in EnumerateReferencedTypes(semanticModel, root))
            {
                if (!IsForbiddenWorkoutProgressDependency(referencedType))
                {
                    continue;
                }

                var violation = new Violation(
                    ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, syntaxTree.FilePath)),
                    GetMetadataName(referencedType));
                violations.TryAdd(violation.Identity, violation);
            }
        }

        return violations.Values.OrderBy(violation => violation.Identity, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateReferencedTypes(SemanticModel semanticModel, CompilationUnitSyntax root)
    {
        var relevantNodes = root.DescendantNodes().OfType<TypeSyntax>().Cast<SyntaxNode>()
            .Concat(root.DescendantNodes().OfType<IdentifierNameSyntax>());

        foreach (var node in relevantNodes)
        {
            var symbol = semanticModel.GetSymbolInfo(node).Symbol;
            var referencedType = ArchitectureTestHelpers.GetOwnedNamedTypeSymbol(symbol)
                ?? ArchitectureTestHelpers.GetOwnedNamedTypeSymbol(semanticModel.GetTypeInfo(node).Type);
            if (referencedType != null)
            {
                yield return referencedType.OriginalDefinition;
            }
        }
    }

    private static bool IsForbiddenWorkoutProgressDependency(INamedTypeSymbol type)
    {
        if (ArchitectureTestHelpers.TryGetPersistedEntityOwner(type, out var owner) &&
            owner == ArchitectureTestHelpers.WorkoutProgressModuleName)
        {
            return true;
        }

        return type.Name.EndsWith("Repository", StringComparison.Ordinal) &&
               ArchitectureTestHelpers.GetCanonicalModuleNameForSymbol(type) == ArchitectureTestHelpers.WorkoutProgressModuleName;
    }

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static string NormalizeFullPath(string path)
    {
        return ArchitectureTestHelpers.NormalizePath(Path.GetFullPath(path));
    }

    private static string BuildViolationMessage(IReadOnlyList<Violation> violations)
    {
        var details = violations.Count == 0
            ? "No direct Workout & Progress persistence or legacy adapter dependencies were found."
            : string.Join(Environment.NewLine, violations.Select(violation => violation.ToString()));

        return "Reporting may publish the accepted-progress contract but must not persist Workout & Progress data or retain the temporary measurement writer.\n" +
               details;
    }

    private sealed record Violation(string SourceFile, string TargetMetadataName)
    {
        public string Identity => $"{SourceFile}|{TargetMetadataName}";

        public override string ToString() => $"{SourceFile} references {TargetMetadataName}";
    }
}
