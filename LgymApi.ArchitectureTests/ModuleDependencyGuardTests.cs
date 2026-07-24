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
        var (repoRoot, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application", "LgymApi.Infrastructure");
        var treeModules = syntaxTrees
            .Select(tree => new SyntaxTreeModule(tree, ResolveDependencyGuardModule(tree.FilePath, repoRoot)))
            .Where(entry => entry.ModuleName is not null)
            .ToList();

        var ownedTypeMap = CollectOwnedTypeMap(compilation, treeModules);
        var observedViolations = CollectObservedViolations(repoRoot, compilation, treeModules, ownedTypeMap);

        ArchitectureTestHelpers.AssertNoUnexpectedModuleBoundaryViolations(GuardId, observedViolations);
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
                    || IsAllowedDependency(sourceModule, targetOwnership))
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

    private static bool ShouldIgnoreSourceFile(string filePath, string repoRoot)
    {
        var relativePath = ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, filePath));

        return relativePath.Equals("LgymApi.Application/ServiceCollectionExtensions.cs", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("LgymApi.Infrastructure/ServiceCollectionExtensions.cs", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("LgymApi.Infrastructure/PlatformServiceCollectionExtensions.cs", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("LgymApi.Infrastructure/Data/Configurations/AppDbContextEntityTypeConfigurationRegistrar.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveDependencyGuardModule(string filePath, string repoRoot)
    {
        if (ShouldIgnoreSourceFile(filePath, repoRoot))
        {
            return null;
        }

        var relativePath = ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, filePath));

        return relativePath switch
        {
            "LgymApi.Application/Repositories/IUserRepository.cs" or
            "LgymApi.Application/Repositories/IUserExternalLoginRepository.cs" or
            "LgymApi.Application/Repositories/IPasswordResetTokenRepository.cs" or
            "LgymApi.Application/Repositories/IRoleRepository.cs" or
            "LgymApi.Application/Repositories/ITutorialProgressRepository.cs"
                => ArchitectureTestHelpers.IdentityModuleName,
            "LgymApi.Application/Repositories/IInAppNotificationRepository.cs" or
            "LgymApi.Application/Notifications/Repositories/IPushInstallationRepository.cs" or
            "LgymApi.Application/Repositories/IPushNotificationMessageRepository.cs"
                => ArchitectureTestHelpers.NotificationsModuleName,
            "LgymApi.Application/Repositories/IReportingRepository.cs" or
            "LgymApi.Application/Repositories/IRecurringReportAssignmentRepository.cs" or
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

    private sealed record SyntaxTreeModule(SyntaxTree Tree, string? ModuleName);

    private sealed record OwnedType(string ModuleName, string FilePath, string DisplayName);
}
