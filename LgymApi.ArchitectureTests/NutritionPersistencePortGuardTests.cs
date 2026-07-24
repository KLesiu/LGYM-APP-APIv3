using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NutritionPersistencePortGuardTests
{
    private sealed record PersistenceSeam(
        string PortFile,
        string RepositoryFile,
        IReadOnlySet<string> TrackedMethods,
        IReadOnlySet<string> ReadMethods);

    private static readonly PersistenceSeam[] Seams =
    [
        new(
            "LgymApi.Application/Nutrition/Persistence/IDietPlanPersistence.cs",
            "LgymApi.Infrastructure/Repositories/Nutrition/DietPlanPersistenceRepository.cs",
            new HashSet<string>(StringComparer.Ordinal) { "FindTrackedPlanByIdAsync" },
            new HashSet<string>(StringComparer.Ordinal)
            {
                "GetPlanByIdAsync", "ListPlansByTrainerAndTraineeAsync", "ListActivePlansForTraineeAsync",
                "GetActivePlanForTraineeAsync", "ListPlanHistoryAsync"
            }),
        new(
            "LgymApi.Application/Nutrition/Persistence/ISupplementationPersistence.cs",
            "LgymApi.Infrastructure/Repositories/Nutrition/SupplementationPersistenceRepository.cs",
            new HashSet<string>(StringComparer.Ordinal)
            {
                "FindTrackedPlanByIdAsync", "ListTrackedPlansByTrainerAndTraineeAsync",
                "GetTrackedActivePlanForTraineeAsync", "FindTrackedIntakeLogAsync"
            },
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ListPlansByTrainerAndTraineeAsync", "GetActivePlanForTraineeAsync",
                "ListIntakeLogsForPlanAsync", "FindIntakeLogAsync"
            })
    ];

    [Test]
    public void Nutrition_Persistence_Ports_Adapters_And_Registrations_Should_Be_Module_Local()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var missing = Seams
            .SelectMany(seam => new[] { seam.PortFile, seam.RepositoryFile })
            .Where(path => !File.Exists(Path.Combine(root, path)))
            .ToList();
        var registrationSource = File.ReadAllText(Path.Combine(root, "LgymApi.Infrastructure/NutritionServiceCollectionExtensions.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Empty, "Nutrition persistence seams must stay under their module-owned paths.");
            Assert.That(registrationSource, Does.Contain("AddScoped<IDietPlanPersistence, DietPlanPersistenceRepository>()"));
            Assert.That(registrationSource, Does.Contain("AddScoped<ISupplementationPersistence, SupplementationPersistenceRepository>()"));
        });
    }

    [Test]
    public void Nutrition_Persistence_Adapters_Should_Remain_Stage_Only_And_Preserve_Tracking_Split()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var violations = Seams.SelectMany(seam => CollectViolations(seam, root)).ToList();

        Assert.That(
            violations,
            Is.Empty,
            "Nutrition persistence adapters must stage writes only, keep mutation loads tracked, and use AsNoTracking for reads:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [TestCase("_dbContext.SaveChangesAsync()", "SaveChangesAsync")]
    [TestCase("_dbContext.Database.BeginTransactionAsync()", "BeginTransactionAsync")]
    public void Nutrition_Persistence_Adapter_Syntax_Should_Reject_Commit_Or_Transaction_Ownership(
        string statement,
        string expectedDiagnostic)
    {
        var source = $$"""
            public sealed class Repository
            {
                public object Execute(dynamic _dbContext) => {{statement}};
            }
            """;

        Assert.That(CollectStageOnlyViolations(source), Is.EqualTo(new[] { expectedDiagnostic }));
    }

    private static IEnumerable<string> CollectViolations(PersistenceSeam seam, string root)
    {
        var relativePath = seam.RepositoryFile;
        var source = File.ReadAllText(Path.Combine(root, relativePath));
        foreach (var violation in CollectStageOnlyViolations(source))
        {
            yield return $"{relativePath}: {violation}";
        }

        var methods = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>()
            .ToDictionary(method => method.Identifier.ValueText, StringComparer.Ordinal);
        foreach (var methodName in seam.TrackedMethods)
        {
            if (!methods.TryGetValue(methodName, out var method) || method.ToFullString().Contains("AsNoTracking", StringComparison.Ordinal))
            {
                yield return $"{relativePath}: tracked method '{methodName}' must not use AsNoTracking.";
            }
        }

        foreach (var methodName in seam.ReadMethods)
        {
            if (!methods.TryGetValue(methodName, out var method) || !method.ToFullString().Contains("AsNoTracking", StringComparison.Ordinal))
            {
                yield return $"{relativePath}: read method '{methodName}' must use AsNoTracking.";
            }
        }
    }

    private static IReadOnlyList<string> CollectStageOnlyViolations(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        return root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Select(access => access.Name.Identifier.ValueText)
            .Where(name => name is "SaveChanges" or "SaveChangesAsync" or "BeginTransaction" or "BeginTransactionAsync" or "Commit" or "CommitAsync" or "Rollback" or "RollbackAsync")
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
