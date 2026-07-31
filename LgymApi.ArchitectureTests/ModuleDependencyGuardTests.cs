using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModuleDependencyGuardTests
{
    private const string GuardId = nameof(ModuleDependencyGuardTests);
    private const string NutritionCoachingContract = "LgymApi.Application.Coaching.Contracts.Access.ICoachingRelationshipAccessService";

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedDependencies =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [ArchitectureTestHelpers.PlatformModuleName] = CreateAllowedSet(),
            [ArchitectureTestHelpers.IdentityModuleName] = CreateAllowedSet(ArchitectureTestHelpers.PlatformModuleName),
            [ArchitectureTestHelpers.NotificationsModuleName] = CreateAllowedSet(ArchitectureTestHelpers.PlatformModuleName, ArchitectureTestHelpers.IdentityModuleName),
            [ArchitectureTestHelpers.ReportingModuleName] = CreateAllowedSet(ArchitectureTestHelpers.PlatformModuleName, ArchitectureTestHelpers.IdentityModuleName, ArchitectureTestHelpers.CoachingModuleName, ArchitectureTestHelpers.TrainingPlanningModuleName),
            [ArchitectureTestHelpers.TrainingPlanningModuleName] = CreateAllowedSet(ArchitectureTestHelpers.PlatformModuleName, ArchitectureTestHelpers.IdentityModuleName),
            [ArchitectureTestHelpers.WorkoutProgressModuleName] = CreateAllowedSet(ArchitectureTestHelpers.PlatformModuleName, ArchitectureTestHelpers.IdentityModuleName, ArchitectureTestHelpers.TrainingPlanningModuleName),
            [ArchitectureTestHelpers.CoachingModuleName] = CreateAllowedSet(ArchitectureTestHelpers.PlatformModuleName, ArchitectureTestHelpers.IdentityModuleName, ArchitectureTestHelpers.TrainingPlanningModuleName, ArchitectureTestHelpers.WorkoutProgressModuleName, ArchitectureTestHelpers.NotificationsModuleName),
            [ArchitectureTestHelpers.NutritionModuleName] = CreateAllowedSet(ArchitectureTestHelpers.PlatformModuleName, ArchitectureTestHelpers.IdentityModuleName, ArchitectureTestHelpers.CoachingModuleName)
        };

    [Test]
    public void Module_Dependency_Graph_Should_Follow_Documented_Eight_Module_Matrix()
    {
        var scan = ModuleBoundaryProductionScan.Prepare();
        var treeModules = scan.SyntaxTrees
            .Select(tree => new SyntaxTreeModule(tree, ResolveDependencyGuardModule(tree, scan.RepoRoot)))
            .Where(entry => entry.ModuleName is not null)
            .ToList();

        var ownedTypeMap = CollectOwnedTypeMap(scan.Compilation, treeModules);
        var observedViolations = CollectObservedViolations(scan.RepoRoot, scan.Compilation, treeModules, ownedTypeMap);

        TestContext.Progress.WriteLine($"Module dependency scan: {scan.DescribeSourceTreeCounts()}; violations={observedViolations.Count}.");
        Assert.That(
            observedViolations,
            Is.Empty,
            ModuleBoundaryObservedViolation.DescribeAll(observedViolations));
    }

    [Test]
    public void TrainingPlanning_Relocated_And_Legacy_Source_Fixtures_Should_Be_Observed()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var repositoriesTree = CSharpSyntaxTree.ParseText(
            """
            namespace Task20Fixture.Repositories;

            public interface IExerciseRepository { }
            public interface ITrainingRepository { }
            """,
            path: Path.Combine(repoRoot, "LgymApi.Application", "Repositories", "IExerciseRepository.cs"));
        var relocatedTree = CSharpSyntaxTree.ParseText(
            """
            using Task20Fixture.Repositories;

            namespace LgymApi.Application.TrainingPlanning.Relocated;

            internal sealed class RelocatedPlanningService
            {
                private readonly IExerciseRepository _exercises = default!;
            }
            """,
            path: Path.Combine(repoRoot, "LgymApi.TrainingPlanning", "Relocated", "RelocatedPlanningService.cs"));
        var legacyTree = CSharpSyntaxTree.ParseText(
            """
            using Task20Fixture.Repositories;

            namespace LgymApi.Application.Features.PlanDay;

            internal sealed class LegacyPlanDayAdapter
            {
                private readonly ITrainingRepository _trainings = default!;
            }
            """,
            path: Path.Combine(repoRoot, "LgymApi.Application", "LegacyPlanning", "LegacyPlanDayAdapter.cs"));
        List<SyntaxTree> syntaxTrees = [repositoriesTree, relocatedTree, legacyTree];
        var compilation = ArchitectureTestHelpers.CreateCompilation(syntaxTrees);
        var treeModules = syntaxTrees
            .Select(tree => new SyntaxTreeModule(tree, ResolveDependencyGuardModule(tree, repoRoot)))
            .Where(entry => entry.ModuleName is not null)
            .ToArray();

        var violations = CollectObservedViolations(
            repoRoot,
            compilation,
            treeModules,
            CollectOwnedTypeMap(compilation, treeModules));
        var assertionFailure = Assert.Throws<AssertionException>(() => Assert.That(
            violations,
            Is.Empty,
            ModuleBoundaryObservedViolation.DescribeAll(violations)));

        Assert.Multiple(() =>
        {
            Assert.That(violations, Has.Count.EqualTo(2));
            Assert.That(violations, Has.All.Matches<ModuleBoundaryObservedViolation>(violation =>
                violation.SourceModule == ArchitectureTestHelpers.TrainingPlanningModuleName
                && violation.TargetModule == ArchitectureTestHelpers.WorkoutProgressModuleName));
            Assert.That(violations.Select(violation => violation.TargetSymbolOrPath), Has.Some.Contains("IExerciseRepository"));
            Assert.That(violations.Select(violation => violation.TargetSymbolOrPath), Has.Some.Contains("ITrainingRepository"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.Some.Contains("LgymApi.TrainingPlanning/Relocated/RelocatedPlanningService.cs"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.Some.Contains("LgymApi.Application/LegacyPlanning/LegacyPlanDayAdapter.cs"));
            foreach (var violation in violations)
            {
                Assert.That(assertionFailure!.Message, Does.Contain(violation.SourceSymbolOrPath));
                Assert.That(assertionFailure.Message, Does.Contain(violation.TargetSymbolOrPath));
            }
        });
    }

    private static readonly object[] FormerDebtGroupFixtures =
    {
        new object[]
        {
            "LgymApi.Application/Relocated/FormerReportingWorkoutDebt.cs",
            "LgymApi.Application.Features.Reporting.Relocated",
            ArchitectureTestHelpers.ReportingModuleName,
            "LgymApi.Application/WorkoutProgress/Contracts/ReportingIntegration/FormerWorkoutDependency.cs",
            "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration",
            ArchitectureTestHelpers.WorkoutProgressModuleName
        },
        new object[]
        {
            "LgymApi.TrainingPlanning/FormerDebt/FormerPlanningWorkoutDebt.cs",
            "LgymApi.Application.TrainingPlanning.FormerDebt",
            ArchitectureTestHelpers.TrainingPlanningModuleName,
            "LgymApi.Application/WorkoutProgress/Contracts/FormerWorkoutDependency.cs",
            "LgymApi.Application.WorkoutProgress.Contracts",
            ArchitectureTestHelpers.WorkoutProgressModuleName
        }
    };

    [TestCaseSource(nameof(FormerDebtGroupFixtures))]
    public void Every_Former_Debt_Group_Should_Produce_An_Observed_Violation(
        string sourcePath,
        string sourceNamespace,
        string expectedSourceModule,
        string targetPath,
        string targetNamespace,
        string expectedTargetModule)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var violations = CollectDependencyFixtureViolations(
            repoRoot,
            sourcePath,
            sourceNamespace,
            targetPath,
            targetNamespace);
        var assertionFailure = Assert.Throws<AssertionException>(() => Assert.That(
            violations,
            Is.Empty,
            ModuleBoundaryObservedViolation.DescribeAll(violations)));

        Assert.Multiple(() =>
        {
            Assert.That(
                violations,
                Has.Some.Matches<ModuleBoundaryObservedViolation>(violation =>
                violation.SourceModule == expectedSourceModule
                && violation.TargetModule == expectedTargetModule
                && violation.TargetSymbolOrPath.Contains("FormerDebtDependency", StringComparison.Ordinal)));
            Assert.That(assertionFailure!.Message, Does.Contain($"Source module: {expectedSourceModule}"));
            Assert.That(assertionFailure.Message, Does.Contain($"Target module: {expectedTargetModule}"));
            Assert.That(assertionFailure.Message, Does.Contain(sourcePath));
            Assert.That(assertionFailure.Message, Does.Contain("FormerDebtDependency"));
        });
    }

    [Test]
    public void Helper_Folder_Source_Should_Be_Observed_And_Fail_Directly()
    {
        const string sourcePath = "LgymApi.Application/Features/Reporting/Helpers/HiddenReportingDebt.cs";
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var violations = CollectDependencyFixtureViolations(
            repoRoot,
            sourcePath,
            "LgymApi.Application.Features.Reporting.Helpers",
            "LgymApi.Application/WorkoutProgress/Contracts/ReportingIntegration/HiddenWorkoutDependency.cs",
            "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration");
        var assertionFailure = Assert.Throws<AssertionException>(() => Assert.That(
            violations,
            Is.Empty,
            ModuleBoundaryObservedViolation.DescribeAll(violations)));

        Assert.Multiple(() =>
        {
            Assert.That(violations, Has.Count.EqualTo(1));
            Assert.That(violations[0].SourceModule, Is.EqualTo(ArchitectureTestHelpers.ReportingModuleName));
            Assert.That(violations[0].TargetModule, Is.EqualTo(ArchitectureTestHelpers.WorkoutProgressModuleName));
            Assert.That(assertionFailure!.Message, Does.Contain(sourcePath));
            Assert.That(assertionFailure.Message, Does.Contain("HiddenWorkoutDependency"));
        });
    }

    [Test]
    public void Platform_Module_Should_Not_Allow_Canonical_Module_Dependencies()
    {
        Assert.That(
            AllowedDependencies[ArchitectureTestHelpers.PlatformModuleName],
            Is.Empty,
            "Platform is a canonical module with no allowed canonical-module targets.");
    }

    [Test]
    public void Main_Record_Repository_Should_Belong_To_Workout_And_Progress()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var repositoryPath = Path.Combine(repoRoot, "LgymApi.Application", "Repositories", "IMainRecordRepository.cs");

        Assert.That(
            ResolveDependencyGuardModule(repositoryPath, repoRoot),
            Is.EqualTo(ArchitectureTestHelpers.WorkoutProgressModuleName));
    }

    [Test]
    public void Elo_Registry_Repository_Should_Belong_To_Workout_And_Progress()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var repositoryPath = Path.Combine(repoRoot, "LgymApi.Application", "Repositories", "IEloRegistryRepository.cs");

        Assert.That(
            ResolveDependencyGuardModule(repositoryPath, repoRoot),
            Is.EqualTo(ArchitectureTestHelpers.WorkoutProgressModuleName));
    }

    private static Dictionary<INamedTypeSymbol, OwnedType> CollectOwnedTypeMap(
        Compilation compilation,
        IEnumerable<SyntaxTreeModule> treeModules)
    {
        var ownedTypeMap = new Dictionary<INamedTypeSymbol, OwnedType>(SymbolEqualityComparer.Default);

        foreach (var treeModule in treeModules)
        {
            var semanticModel = compilation.GetSemanticModel(treeModule.Tree, ignoreAccessibility: true);
            var root = treeModule.Tree.GetCompilationUnitRoot();

            foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol declaredSymbol)
                {
                    continue;
                }

                ownedTypeMap[declaredSymbol] = new OwnedType(
                    treeModule.ModuleName!,
                    treeModule.Tree.FilePath,
                    declaredSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
            }
        }

        return ownedTypeMap;
    }

    private static IReadOnlyList<ModuleBoundaryObservedViolation> CollectDependencyFixtureViolations(
        string repoRoot,
        string sourcePath,
        string sourceNamespace,
        string targetPath,
        string targetNamespace)
    {
        var targetTree = CSharpSyntaxTree.ParseText(
            $"namespace {targetNamespace}; public sealed class FormerDebtDependency {{ }}",
            path: Path.Combine(repoRoot, targetPath.Replace('/', Path.DirectorySeparatorChar)));
        var sourceTree = CSharpSyntaxTree.ParseText(
            $$"""
            using {{targetNamespace}};
            namespace {{sourceNamespace}};
            internal sealed class FormerDebtConsumer
            {
                private readonly FormerDebtDependency _dependency = default!;
            }
            """,
            path: Path.Combine(repoRoot, sourcePath.Replace('/', Path.DirectorySeparatorChar)));
        List<SyntaxTree> syntaxTrees = [targetTree, sourceTree];
        var compilation = ArchitectureTestHelpers.CreateCompilation(syntaxTrees);
        var treeModules = syntaxTrees
            .Select(tree => new SyntaxTreeModule(tree, ResolveDependencyGuardModule(tree, repoRoot)))
            .Where(entry => entry.ModuleName is not null)
            .ToArray();

        return CollectObservedViolations(
            repoRoot,
            compilation,
            treeModules,
            CollectOwnedTypeMap(compilation, treeModules));
    }

    private static IReadOnlyList<ModuleBoundaryObservedViolation> CollectObservedViolations(
        string repoRoot,
        Compilation compilation,
        IEnumerable<SyntaxTreeModule> treeModules,
        IReadOnlyDictionary<INamedTypeSymbol, OwnedType> ownedTypeMap)
    {
        var observedViolations = new Dictionary<string, ModuleBoundaryObservedViolation>(StringComparer.Ordinal);

        foreach (var treeModule in treeModules)
        {
            var sourceModule = treeModule.ModuleName!;
            var semanticModel = compilation.GetSemanticModel(treeModule.Tree, ignoreAccessibility: true);
            var root = treeModule.Tree.GetCompilationUnitRoot();
            var normalizedSourcePath = ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, treeModule.Tree.FilePath));

            foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
            {
                var ownedNamedType = ArchitectureTestHelpers.GetOwnedNamedTypeSymbol(semanticModel.GetTypeInfo(typeSyntax).Type);
                if (ownedNamedType == null || !ownedTypeMap.TryGetValue(ownedNamedType, out var targetOwnership))
                {
                    continue;
                }

                if (targetOwnership.ModuleName.Equals(sourceModule, StringComparison.Ordinal)
                    || IsAllowedDependency(sourceModule, targetOwnership)
                    || ArchitectureTestHelpers.MatchesApiAdapterDependencyContract(normalizedSourcePath, targetOwnership.DisplayName))
                {
                    continue;
                }

                var sourceContainer = semanticModel.GetEnclosingSymbol(typeSyntax.SpanStart) as INamedTypeSymbol;
                if (sourceContainer == null)
                {
                    continue;
                }

                var sourceDescriptor = $"{sourceContainer.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)} @ {normalizedSourcePath}";
                var targetDescriptor = $"{targetOwnership.DisplayName} @ {ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, targetOwnership.FilePath))}";

                var violation = new ModuleBoundaryObservedViolation(
                    GuardId,
                    sourceModule,
                    targetOwnership.ModuleName,
                    sourceDescriptor,
                    targetDescriptor);

                observedViolations[violation.IdentityKey] = violation;
            }
        }

        return observedViolations.Values
            .OrderBy(violation => violation.IdentityKey, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsAllowedDependency(string sourceModule, OwnedType targetOwnership)
    {
        if (!AllowedDependencies.TryGetValue(sourceModule, out var allowedTargets))
        {
            throw new AssertionException($"Missing allowed dependency configuration for module '{sourceModule}'.");
        }

        if (!allowedTargets.Contains(targetOwnership.ModuleName))
        {
            return false;
        }

        return !sourceModule.Equals(ArchitectureTestHelpers.NutritionModuleName, StringComparison.Ordinal)
            || !targetOwnership.ModuleName.Equals(ArchitectureTestHelpers.CoachingModuleName, StringComparison.Ordinal)
            || targetOwnership.DisplayName.Equals(NutritionCoachingContract, StringComparison.Ordinal);
    }

    private static IReadOnlySet<string> CreateAllowedSet(params string[] allowedTargets)
    {
        return new HashSet<string>(allowedTargets, StringComparer.Ordinal);
    }

    private static string? ResolveDependencyGuardModule(string filePath, string repoRoot)
    {
        if (ArchitectureTestHelpers.ClassifyModuleBoundaryFile(filePath, repoRoot).IsExcluded)
        {
            return null;
        }

        var relativePath = ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, filePath));

        if (relativePath.StartsWith("LgymApi.TrainingPlanning/", StringComparison.OrdinalIgnoreCase))
        {
            return ArchitectureTestHelpers.TrainingPlanningModuleName;
        }

        return relativePath switch
        {
            "LgymApi.Application/Repositories/IUserRepository.cs" or
            "LgymApi.Application/Repositories/IUserExternalLoginRepository.cs" or
            "LgymApi.Application/Repositories/IPasswordResetTokenRepository.cs" or
            "LgymApi.Application/Repositories/IRoleRepository.cs" or
            "LgymApi.Application/Repositories/ITutorialProgressRepository.cs"
                => ArchitectureTestHelpers.IdentityModuleName,
            "LgymApi.Notifications/IInAppNotificationRepository.cs" or
            "LgymApi.Notifications/Repositories/IPushInstallationRepository.cs" or
            "LgymApi.Notifications/Repositories/IPushNotificationMessageRepository.cs"
                => ArchitectureTestHelpers.NotificationsModuleName,
            "LgymApi.Application/Reporting/Persistence/IReportTemplatePersistence.cs" or
            "LgymApi.Application/Reporting/Persistence/IReportRequestSubmissionPersistence.cs" or
            "LgymApi.Application/Reporting/Persistence/IRecurringReportAssignmentPersistence.cs" or
            "LgymApi.Application/Reporting/Persistence/IReportPhotoPersistence.cs" or
            "LgymApi.Application/Reporting/Persistence/IReportingRelationshipAccessPersistence.cs" or
            "LgymApi.Application/Abstractions/Storage/IPhotoStorageProvider.cs"
                => ArchitectureTestHelpers.ReportingModuleName,
            "LgymApi.Application/Repositories/IPlanRepository.cs" or
            "LgymApi.Application/Repositories/IPlanDayRepository.cs" or
            "LgymApi.Application/Repositories/IPlanDayExerciseRepository.cs"
                => ArchitectureTestHelpers.TrainingPlanningModuleName,
            "LgymApi.Application/Repositories/IGymRepository.cs" or
            "LgymApi.Application/Repositories/ITrainingRepository.cs" or
            "LgymApi.Application/Repositories/IExerciseRepository.cs" or
            "LgymApi.Application/Repositories/IExerciseScoreRepository.cs" or
            "LgymApi.Application/Repositories/ITrainingExerciseScoreRepository.cs" or
            "LgymApi.Application/Repositories/IMeasurementRepository.cs" or
            "LgymApi.Application/Repositories/IEloRegistryRepository.cs" or
            "LgymApi.Application/Repositories/IMainRecordRepository.cs"
                => ArchitectureTestHelpers.WorkoutProgressModuleName,
            "LgymApi.Application/Nutrition/Persistence/IDietPlanPersistence.cs" or
            "LgymApi.Application/Nutrition/Persistence/ISupplementationPersistence.cs"
                => ArchitectureTestHelpers.NutritionModuleName,
            _ => ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(filePath)
        };
    }

    private static string? ResolveDependencyGuardModule(SyntaxTree tree, string repoRoot)
    {
        var pathModule = ResolveDependencyGuardModule(tree.FilePath, repoRoot);
        if (pathModule != null)
        {
            return pathModule;
        }

        return ModuleBoundaryProductionScan.ResolveCanonicalModule(tree, repoRoot);
    }

    private sealed record SyntaxTreeModule(SyntaxTree Tree, string? ModuleName);

    private sealed record OwnedType(string ModuleName, string FilePath, string DisplayName);
}
