using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Identity.Contracts;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CrossModuleEntityLeakageGuardTests
{
    private const string GuardId = "CrossModuleEntityLeakage";
    private const string PlanRepositoryMetadataName = "LgymApi.Application.Repositories.IPlanRepository";
    private const string PlanDayRepositoryMetadataName = "LgymApi.Application.Repositories.IPlanDayRepository";

    private static readonly string[] RemainingTrainingPlanningApplicationAdapters =
    [
        "LgymApi.Application/Task7ApiCompatibility/PlanningNutrition/Adapters/PlanAccountCompatibilityAdapter.cs",
        "LgymApi.Application/Task7ApiCompatibility/PlanningNutrition/Adapters/ManagedPlanAccountCompatibilityAdapter.cs"
    ];

    private static readonly HashSet<string> TrainingPlanningEntityMetadataNames =
    [
        "LgymApi.Domain.Entities.Plan",
        "LgymApi.Domain.Entities.PlanDay",
        "LgymApi.Domain.Entities.User",
        "LgymApi.Domain.Entities.Exercise",
        "LgymApi.Domain.Entities.Training"
    ];

    private static readonly IReadOnlyDictionary<string, string> RepositoryOwnerByMetadataName = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["LgymApi.Application.Repositories.IUserRepository"] = "Identity & Accounts",
        ["LgymApi.Application.Repositories.IRoleRepository"] = "Identity & Accounts",
        ["LgymApi.Application.Repositories.IEloRegistryRepository"] = "Workout & Progress",
        ["LgymApi.Application.Repositories.IInAppNotificationRepository"] = "Notifications",
        ["LgymApi.Application.Notifications.Repositories.IPushInstallationRepository"] = "Notifications",
        ["LgymApi.Application.Repositories.IPushNotificationMessageRepository"] = "Notifications",
        ["LgymApi.Application.Reporting.Persistence.IReportTemplatePersistence"] = "Reporting",
        ["LgymApi.Application.Reporting.Persistence.IReportRequestSubmissionPersistence"] = "Reporting",
        ["LgymApi.Application.Reporting.Persistence.IRecurringReportAssignmentPersistence"] = "Reporting",
        ["LgymApi.Application.Reporting.Persistence.IReportPhotoPersistence"] = "Reporting",
        ["LgymApi.Application.Reporting.Persistence.IReportingRelationshipAccessPersistence"] = "Reporting",
        ["LgymApi.Application.Repositories.IPlanRepository"] = "Training Planning",
        ["LgymApi.Application.Repositories.IPlanDayRepository"] = "Training Planning",
        ["LgymApi.Application.Repositories.IPlanDayExerciseRepository"] = "Training Planning",
        ["LgymApi.Application.Repositories.IGymRepository"] = "Workout & Progress",
        ["LgymApi.Application.Repositories.ITrainingRepository"] = "Workout & Progress",
        ["LgymApi.Application.Repositories.IExerciseRepository"] = "Workout & Progress",
        ["LgymApi.Application.Repositories.IExerciseScoreRepository"] = "Workout & Progress",
        ["LgymApi.Application.Repositories.ITrainingExerciseScoreRepository"] = "Workout & Progress",
        ["LgymApi.Application.Repositories.IMainRecordRepository"] = "Workout & Progress",
        ["LgymApi.Application.Repositories.IMeasurementRepository"] = "Workout & Progress",
        ["LgymApi.Application.Nutrition.Persistence.IDietPlanPersistence"] = "Nutrition",
        ["LgymApi.Application.Nutrition.Persistence.ISupplementationPersistence"] = "Nutrition"
    };

    [TestCase("LgymApi.Application.Repositories.IEloRegistryRepository", "Workout & Progress")]
    [TestCase("LgymApi.Application.Repositories.IMainRecordRepository", "Workout & Progress")]
    public void Moved_Repositories_Should_Use_Canonical_Owners(string metadataName, string expectedOwner)
    {
        Assert.That(RepositoryOwnerByMetadataName[metadataName], Is.EqualTo(expectedOwner));
    }

    [TestCase("LgymApi.Application/EloRegistry/EloRegistryService.cs", "Workout & Progress")]
    [TestCase("LgymApi.Application/MainRecords/MainRecordsService.cs", "Workout & Progress")]
    [TestCase("LgymApi.Application/WorkoutProgress/Contracts/WorkoutProgressContract.cs", "Workout & Progress")]
    public void Moved_Application_Paths_Should_Use_Canonical_Owners(string path, string expectedOwner)
    {
        Assert.That(TryGetApplicationModuleName(path), Is.EqualTo(expectedOwner));
    }

    [Test]
    public void Direct_Foreign_Entity_Exposure_Should_Fail_While_Typed_Ids_Are_Allowed()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var applicationTree = CSharpSyntaxTree.ParseText("""
            using LgymApi.Domain.ValueObjects;
            using LgymApi.Application.Repositories;
            using PlanEntity = LgymApi.Domain.Entities.Plan;
            using UserEntity = LgymApi.Domain.Entities.User;

            namespace LgymApi.Application.Repositories
            {
                public interface IPlanRepository { }
            }

            namespace LgymApi.Application.Features.Reporting
            {
                public sealed class ForeignEntityExposure
                {
                    public UserEntity ForeignUser { get; init; }
                    public Id<UserEntity> ForeignUserId { get; init; }
                    public Id<LgymApi.Domain.Entities.User> FullyQualifiedForeignUserId { get; init; }
                    public PlanEntity ForeignPlan { get; init; }
                    public Id<PlanEntity> ForeignPlanId { get; init; }
                    public UserEntity[] ForeignUserCollection { get; init; }
                    public ForeignWrapper<PlanEntity> ForeignPlanWrapper { get; init; }
                    public IUserRepository ForeignUserRepository { get; init; }
                    public IPlanRepository ForeignPlanRepository { get; init; }

                    public UserEntity ReturnForeignUser() => default;

                    public void AcceptForeignUser(UserEntity user)
                    {
                    }
                }

                public sealed class ForeignWrapper<T>
                {
                }
            }
            """, path: Path.Combine(repoRoot, "LgymApi.Application", "Features", "Reporting", "ForeignEntityExposure.cs"));
        var compilation = CSharpCompilation.Create(
            "CrossModuleEntityLeakageFixture",
            [applicationTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(User).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(AccountReference).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var violations = CollectViolations(
            compilation,
            [applicationTree],
            repoRoot);

        TestContext.Progress.WriteLine(
            $"Typed fixture source accepts only Id<T> transport while rejecting direct entity and repository values.");

        Assert.Multiple(() =>
        {
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.Some.Contains("ForeignUser"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.Some.Contains("ForeignPlan"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.Some.Contains("ForeignUserCollection"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.Some.Contains("ForeignPlanWrapper"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.Some.Contains("ForeignUserRepository"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.Some.Contains("ForeignPlanRepository"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.Some.Contains("ReturnForeignUser"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.Some.Contains("AcceptForeignUser"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.None.Contains("ForeignUserId"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.None.Contains("FullyQualifiedForeignUserId"));
            Assert.That(violations.Select(violation => violation.SourceSymbolOrPath), Has.None.Contains("ForeignPlanId"));
            Assert.That(violations.Select(violation => violation.TargetSymbolOrPath), Has.Some.Contains(typeof(User).FullName));
            Assert.That(violations.Select(violation => violation.TargetSymbolOrPath), Has.Some.Contains(typeof(Plan).FullName));
            Assert.That(violations.Select(violation => violation.TargetSymbolOrPath), Has.Some.Contains("LgymApi.Application.Repositories.IUserRepository"));
            Assert.That(violations.Select(violation => violation.TargetSymbolOrPath), Has.Some.Contains(PlanRepositoryMetadataName));
        });
    }

    private static readonly object[] FormerEntityDebtGroupFixtures =
    {
        new object[]
        {
            "LgymApi.Application/Relocated/FormerReportingIdentityDebt.cs",
            "LgymApi.Application.Features.Reporting.Relocated",
            ArchitectureTestHelpers.ReportingModuleName
        },
        new object[]
        {
            "LgymApi.Application/WorkoutProgress/FormerWorkoutIdentityDebt.cs",
            "LgymApi.Application.WorkoutProgress",
            ArchitectureTestHelpers.WorkoutProgressModuleName
        },
        new object[]
        {
            "LgymApi.TrainingPlanning/FormerDebt/FormerPlanningIdentityDebt.cs",
            "LgymApi.Application.TrainingPlanning.FormerDebt",
            ArchitectureTestHelpers.TrainingPlanningModuleName
        }
    };

    [TestCaseSource(nameof(FormerEntityDebtGroupFixtures))]
    public void Every_Former_Identity_Entity_Debt_Group_Should_Produce_An_Observed_Violation(
        string sourcePath,
        string sourceNamespace,
        string expectedSourceModule)
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var sourceTree = CSharpSyntaxTree.ParseText(
            $$"""
            using UserEntity = LgymApi.Domain.Entities.User;
            namespace {{sourceNamespace}};
            internal sealed class FormerIdentityDebtConsumer
            {
                public UserEntity DirectEntity { get; } = default!;
            }
            """,
            path: Path.Combine(repoRoot, sourcePath.Replace('/', Path.DirectorySeparatorChar)));
        var compilation = CSharpCompilation.Create(
            "FormerIdentityDebtFixture",
            [sourceTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(User).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var violations = CollectViolations(compilation, [sourceTree], repoRoot);

        Assert.That(
            violations,
            Has.Some.Matches<ModuleBoundaryObservedViolation>(violation =>
                violation.SourceModule == expectedSourceModule
                && violation.TargetModule == ArchitectureTestHelpers.IdentityModuleName
                && violation.TargetSymbolOrPath == typeof(User).FullName));
    }

    [Test]
    public void Nested_Generic_Foreign_Entity_Should_Fail_While_Nested_Marker_Id_Is_Allowed()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var sourceTree = CSharpSyntaxTree.ParseText(
            """
            using System.Collections.Generic;
            using LgymApi.Domain.Entities;
            using LgymApi.Domain.ValueObjects;
            namespace LgymApi.Application.Features.Reporting.Relocated;
            internal sealed class NestedEntityDebt
            {
                public IReadOnlyDictionary<string, IReadOnlyList<User>> DirectNestedEntities { get; } = default!;
                public IReadOnlyDictionary<string, IReadOnlyList<Id<User>>> NestedMarkerIds { get; } = default!;
            }
            """,
            path: Path.Combine(repoRoot, "LgymApi.Application", "Relocated", "NestedEntityDebt.cs"));
        var compilation = CSharpCompilation.Create(
            "NestedEntityDebtFixture",
            [sourceTree],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(User).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var violations = CollectViolations(compilation, [sourceTree], repoRoot);

        Assert.Multiple(() =>
        {
            Assert.That(
                violations,
                Has.Some.Matches<ModuleBoundaryObservedViolation>(violation =>
                    violation.SourceSymbolOrPath.Contains("DirectNestedEntities", StringComparison.Ordinal)
                    && violation.TargetSymbolOrPath == typeof(User).FullName));
            Assert.That(
                violations,
                Has.None.Matches<ModuleBoundaryObservedViolation>(violation =>
                    violation.SourceSymbolOrPath.Contains("NestedMarkerIds", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void TrainingPlanningRepositoriesAndPublicContracts_ShouldRemainInternalAndMarkerSafe()
    {
        var (_, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.TrainingPlanning");
        var planRepository = compilation.GetTypeByMetadataName(PlanRepositoryMetadataName);
        var planDayRepository = compilation.GetTypeByMetadataName(PlanDayRepositoryMetadataName);
        var violations = CollectTrainingPlanningPublicSurfaceViolations(compilation, syntaxTrees);

        Assert.Multiple(() =>
        {
            Assert.That(planRepository, Is.Not.Null);
            Assert.That(planRepository!.DeclaredAccessibility, Is.EqualTo(Accessibility.Internal));
            Assert.That(planDayRepository, Is.Not.Null);
            Assert.That(planDayRepository!.DeclaredAccessibility, Is.EqualTo(Accessibility.Internal));
            Assert.That(
                violations,
                Is.Empty,
                "Training Planning public contracts must remain marker-only, and their implementations and repositories must remain internal.");
        });
    }

    [Test]
    public void TrainingPlanningPublicSurfaceFixture_WithRecursiveLeaks_IsRejected()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            using System.Collections.Generic;
            using LgymApi.Domain.ValueObjects;
            using PlanEntity = LgymApi.Domain.Entities.Plan;
            using PlanDayEntity = LgymApi.Domain.Entities.PlanDay;
            using UserEntity = LgymApi.Domain.Entities.User;
            using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
            using TrainingEntity = LgymApi.Domain.Entities.Training;

            namespace LgymApi.Application.Repositories
            {
                public interface IPlanRepository { }
                public interface IPlanDayRepository { }
                public interface IRoleRepository { }
                public interface IExerciseRepository { }
                public interface ITrainingRepository { }
            }

            namespace LgymApi.Application.TrainingPlanning.Contracts
            {
                using LgymApi.Application.Repositories;

                public sealed record PublicWrapper<T>(T Value);

                public interface ILeakyPlanningUseCase
                {
                    PublicWrapper<IReadOnlyList<PlanEntity>> Plans { get; }
                    PlanDayEntity Day { get; }
                    UserEntity Owner { get; }
                    Id<UserEntity> OwnerId { get; }
                    PublicWrapper<IReadOnlyDictionary<UserEntity, IReadOnlyList<ExerciseEntity>>> NestedForeignTypes { get; }
                    ExerciseEntity Exercise { get; }
                    TrainingEntity Training { get; }
                    IPlanRepository PlansRepository { get; }
                    IPlanDayRepository PlanDaysRepository { get; }
                    IRoleRepository RolesRepository { get; }
                    IExerciseRepository ExerciseRepository { get; }
                    ITrainingRepository TrainingRepository { get; }
                }

                public sealed class PublicImplementation : ILeakyPlanningUseCase
                {
                    public PublicWrapper<IReadOnlyList<PlanEntity>> Plans { get; } = default!;
                    public PlanDayEntity Day { get; } = default!;
                    public UserEntity Owner { get; } = default!;
                    public Id<UserEntity> OwnerId { get; } = default!;
                    public PublicWrapper<IReadOnlyDictionary<UserEntity, IReadOnlyList<ExerciseEntity>>> NestedForeignTypes { get; } = default!;
                    public ExerciseEntity Exercise { get; } = default!;
                    public TrainingEntity Training { get; } = default!;
                    public IPlanRepository PlansRepository { get; } = default!;
                    public IPlanDayRepository PlanDaysRepository { get; } = default!;
                    public IRoleRepository RolesRepository { get; } = default!;
                    public IExerciseRepository ExerciseRepository { get; } = default!;
                    public ITrainingRepository TrainingRepository { get; } = default!;
                }
            }
            """, path: "LgymApi.TrainingPlanning/Contracts/RecursiveLeakFixture.cs");
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);

        var violations = CollectTrainingPlanningPublicSurfaceViolations(compilation, [tree]);

        Assert.Multiple(() =>
        {
            Assert.That(violations.Count(violation => violation.Category == "public repository declaration"), Is.EqualTo(2));
            Assert.That(violations.Select(violation => violation.Target), Does.Contain("LgymApi.Domain.Entities.Plan"));
            Assert.That(violations.Select(violation => violation.Target), Does.Contain("LgymApi.Domain.Entities.PlanDay"));
            Assert.That(violations.Select(violation => violation.Target), Does.Contain("LgymApi.Domain.Entities.User"));
            Assert.That(violations.Select(violation => violation.Target), Does.Contain("LgymApi.Domain.Entities.Exercise"));
            Assert.That(violations.Select(violation => violation.Target), Does.Contain("LgymApi.Domain.Entities.Training"));
            Assert.That(violations.Select(violation => violation.Target), Does.Contain(PlanRepositoryMetadataName));
            Assert.That(violations.Select(violation => violation.Target), Does.Contain(PlanDayRepositoryMetadataName));
            Assert.That(violations.Select(violation => violation.Target), Does.Contain("LgymApi.Application.Repositories.IRoleRepository"));
            Assert.That(violations.Select(violation => violation.Target), Does.Contain("LgymApi.Application.Repositories.IExerciseRepository"));
            Assert.That(violations.Select(violation => violation.Target), Does.Contain("LgymApi.Application.Repositories.ITrainingRepository"));
            Assert.That(violations, Has.Some.Matches<TrainingPlanningPublicSurfaceViolation>(violation =>
                violation.Source.EndsWith(".OwnerId", StringComparison.Ordinal)
                && violation.Target == "LgymApi.Domain.Entities.User"));
            Assert.That(violations, Has.Some.Matches<TrainingPlanningPublicSurfaceViolation>(violation =>
                violation.Source.EndsWith(".NestedForeignTypes", StringComparison.Ordinal)
                && violation.Target == "LgymApi.Domain.Entities.Exercise"));
            Assert.That(violations, Has.Some.Matches<TrainingPlanningPublicSurfaceViolation>(violation =>
                violation.Category == "public implementation"
                && violation.Source.EndsWith("PublicImplementation", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void TrainingPlanning_Production_And_Remaining_Application_Adapters_Should_Have_Zero_Direct_Foreign_Leaks()
    {
        var scan = ModuleBoundaryProductionScan.Prepare();
        var observedSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var violations = CollectViolations(scan.Compilation, scan.SyntaxTrees, scan.RepoRoot, observedSourcePaths)
            .Where(violation => violation.SourceModule == ArchitectureTestHelpers.TrainingPlanningModuleName)
            .ToArray();
        var expectedSourcePaths = Directory
            .EnumerateFiles(Path.Combine(scan.RepoRoot, "LgymApi.TrainingPlanning"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifactPath(path))
            .Select(path => ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(scan.RepoRoot, path)))
            .Concat(RemainingTrainingPlanningApplicationAdapters)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var observedTrainingPlanningSourcePaths = observedSourcePaths
            .Where(IsTrainingPlanningBoundarySourcePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        TestContext.Progress.WriteLine(
            $"Training Planning boundary scan: {scan.DescribeSourceTreeCounts()}; observed={observedTrainingPlanningSourcePaths.Length}; violations={violations.Length}.");

        Assert.Multiple(() =>
        {
            Assert.That(expectedSourcePaths, Has.Some.StartsWith("LgymApi.TrainingPlanning/"));
            Assert.That(observedTrainingPlanningSourcePaths, Is.EqualTo(expectedSourcePaths));
            Assert.That(
                RemainingTrainingPlanningApplicationAdapters.All(adapter => observedTrainingPlanningSourcePaths.Contains(adapter, StringComparer.OrdinalIgnoreCase)),
                Is.True,
                "Every retained Application Planning compatibility adapter must stay inside the semantic scan.");
            Assert.That(
                violations,
                Is.Empty,
                "Training Planning production and retained Application adapters must not directly use foreign entities or repositories.");
        });
    }

    [Test]
    public void Application_Modules_Should_Not_Use_Other_Modules_Domain_Entities_Or_Repositories_Directly()
    {
        var scan = ModuleBoundaryProductionScan.Prepare();
        var violations = CollectViolations(scan.Compilation, scan.SyntaxTrees, scan.RepoRoot);

        TestContext.Progress.WriteLine($"Cross-module entity scan: {scan.DescribeSourceTreeCounts()}; violations={violations.Count}.");

        Assert.Multiple(() =>
        {
            Assert.That(violations, Is.Empty);
            ArchitectureTestHelpers.AssertNoUnexpectedModuleBoundaryViolations(GuardId, violations);

            Assert.That(
                violations.Any(v => v.TargetSymbolOrPath.Contains("Features.", StringComparison.Ordinal)),
                Is.False,
                "Cross-module leakage guard must stay focused on direct entity/repository usage and must not block published contracts/read models/events.");
        });
    }

    private static IReadOnlyList<ModuleBoundaryObservedViolation> CollectViolations(
        CSharpCompilation compilation,
        IEnumerable<SyntaxTree> syntaxTrees,
        string repoRoot,
        ISet<string>? observedSourcePaths = null)
    {
        var observedViolations = new Dictionary<string, ModuleBoundaryObservedViolation>(StringComparer.Ordinal);

        foreach (var tree in syntaxTrees)
        {
            var sourceFile = ArchitectureTestHelpers.ClassifyModuleBoundaryFile(tree.FilePath, repoRoot);
            if (sourceFile.IsExcluded)
            {
                continue;
            }

            var root = tree.GetCompilationUnitRoot();
            var sourceModule = ModuleBoundaryProductionScan.ResolveCanonicalModule(tree, repoRoot)
                ?? TryGetApplicationModuleName(sourceFile.RelativePath)
                ?? TryGetTrainingPlanningModuleFromNamespace(root);
            if (string.IsNullOrWhiteSpace(sourceModule))
            {
                continue;
            }

            observedSourcePaths?.Add(sourceFile.RelativePath);

            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);

            foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
            {
                if (IsTypedEntityIdUsage(typeSyntax, semanticModel)
                    || IsPersistedForeignKeyConfigurationUsage(typeSyntax, semanticModel)
                    || IsTypedEntityIdAliasDeclaration(typeSyntax, semanticModel, root))
                {
                    continue;
                }

                var symbol = semanticModel.GetTypeInfo(typeSyntax).Type;
                if (symbol == null)
                {
                    continue;
                }

                foreach (var referencedType in EnumerateRelevantNamedTypes(symbol))
                {
                    if (!TryResolveTargetOwner(referencedType, out var targetModule))
                    {
                        continue;
                    }

                    if (string.Equals(sourceModule, targetModule, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var sourceSymbol = GetEnclosingSourceSymbol(semanticModel, typeSyntax) ?? sourceFile.RelativePath;
                    var targetSymbol = referencedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
                    var violation = new ModuleBoundaryObservedViolation(GuardId, sourceModule, targetModule, sourceSymbol, targetSymbol);
                    observedViolations.TryAdd(violation.IdentityKey, violation);
                }
            }
        }

        return observedViolations.Values.OrderBy(violation => violation.IdentityKey, StringComparer.Ordinal).ToList();
    }

    private static IReadOnlyList<TrainingPlanningPublicSurfaceViolation> CollectTrainingPlanningPublicSurfaceViolations(
        CSharpCompilation compilation,
        IEnumerable<SyntaxTree> syntaxTrees)
    {
        var violations = new Dictionary<string, TrainingPlanningPublicSurfaceViolation>(StringComparer.Ordinal);

        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol declaredType)
                {
                    continue;
                }

                var declaredTypeName = GetMetadataName(declaredType);
                if (declaredTypeName is PlanRepositoryMetadataName or PlanDayRepositoryMetadataName
                    && declaredType.DeclaredAccessibility != Accessibility.Internal)
                {
                    AddTrainingPlanningViolation(
                        violations,
                        new TrainingPlanningPublicSurfaceViolation(
                            "public repository declaration",
                            declaredTypeName,
                            declaredTypeName));
                }

                if (!IsPubliclyVisible(declaredType))
                {
                    continue;
                }

                if (declaredType.TypeKind == TypeKind.Class
                    && !declaredType.IsAbstract
                    && !declaredType.IsRecord
                    && declaredType.Interfaces.Any(IsTrainingPlanningImplementationContract))
                {
                    AddTrainingPlanningViolation(
                        violations,
                        new TrainingPlanningPublicSurfaceViolation(
                            "public implementation",
                            declaredTypeName,
                            declaredTypeName));
                }

                CollectPublicTypeGraphLeaks(
                    compilation,
                    declaredType,
                    declaredTypeName,
                    violations,
                    new HashSet<string>(StringComparer.Ordinal));
            }
        }

        return violations.Values.OrderBy(violation => violation.Identity, StringComparer.Ordinal).ToList();
    }

    private static void CollectPublicTypeGraphLeaks(
        CSharpCompilation compilation,
        INamedTypeSymbol type,
        string source,
        IDictionary<string, TrainingPlanningPublicSurfaceViolation> violations,
        ISet<string> visitedTypes)
    {
        var metadataName = GetMetadataName(type.OriginalDefinition);
        if (!visitedTypes.Add(metadataName))
        {
            return;
        }

        foreach (var member in type.GetMembers().Where(IsPublicSurfaceMember))
        {
            foreach (var memberType in GetMemberTypes(member))
            {
                foreach (var exposedType in EnumerateNamedTypes(memberType, traverseTypedEntityIds: true))
                {
                    var exposedTypeName = GetMetadataName(exposedType.OriginalDefinition);
                    if (TrainingPlanningEntityMetadataNames.Contains(exposedTypeName)
                        || exposedTypeName is PlanRepositoryMetadataName or PlanDayRepositoryMetadataName
                        || exposedType.Name.EndsWith("Repository", StringComparison.Ordinal))
                    {
                        AddTrainingPlanningViolation(
                            violations,
                            new TrainingPlanningPublicSurfaceViolation(
                                "public contract leak",
                                $"{source}.{member.Name}",
                                exposedTypeName));
                    }

                    if (SymbolEqualityComparer.Default.Equals(exposedType.ContainingAssembly, compilation.Assembly)
                        && IsPubliclyVisible(exposedType))
                    {
                        CollectPublicTypeGraphLeaks(compilation, exposedType, source, violations, visitedTypes);
                    }
                }
            }
        }
    }

    private static IEnumerable<ITypeSymbol> GetMemberTypes(ISymbol member)
    {
        switch (member)
        {
            case IFieldSymbol field:
                yield return field.Type;
                break;
            case IPropertySymbol property:
                yield return property.Type;
                break;
            case IEventSymbol @event:
                yield return @event.Type;
                break;
            case IMethodSymbol method:
                yield return method.ReturnType;
                foreach (var parameter in method.Parameters)
                {
                    yield return parameter.Type;
                }

                foreach (var typeParameter in method.TypeParameters)
                {
                    foreach (var constraintType in typeParameter.ConstraintTypes)
                    {
                        yield return constraintType;
                    }
                }

                break;
        }
    }

    private static bool IsPublicSurfaceMember(ISymbol member)
        => member.DeclaredAccessibility is Accessibility.Public
            or Accessibility.Protected
            or Accessibility.ProtectedOrInternal;

    private static bool IsPubliclyVisible(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTrainingPlanningImplementationContract(INamedTypeSymbol type)
        => type.Name.EndsWith("UseCase", StringComparison.Ordinal)
            || type.Name == "IPlanDayService";

    private static void AddTrainingPlanningViolation(
        IDictionary<string, TrainingPlanningPublicSurfaceViolation> violations,
        TrainingPlanningPublicSurfaceViolation violation)
        => violations.TryAdd(violation.Identity, violation);

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        var typeNames = new Stack<string>();
        for (var current = type; current != null; current = current.ContainingType)
        {
            typeNames.Push(current.MetadataName);
        }

        var namespaceName = type.ContainingNamespace.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? string.Join(".", typeNames)
            : $"{namespaceName}.{string.Join(".", typeNames)}";
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateRelevantNamedTypes(ITypeSymbol symbol)
    {
        foreach (var candidate in EnumerateNamedTypes(symbol))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(
        ITypeSymbol symbol,
        bool traverseTypedEntityIds = false)
    {
        if (symbol is INamedTypeSymbol namedType)
        {
            yield return namedType;

            if (!traverseTypedEntityIds && IsTypedEntityId(namedType))
            {
                yield break;
            }

            foreach (var typeArgument in namedType.TypeArguments)
            {
                foreach (var nested in EnumerateNamedTypes(typeArgument, traverseTypedEntityIds))
                {
                    yield return nested;
                }
            }
        }

        if (symbol.NullableAnnotation != NullableAnnotation.None && symbol is INamedTypeSymbol { TypeArguments.Length: 1 } nullableType)
        {
            foreach (var nested in EnumerateNamedTypes(nullableType.TypeArguments[0], traverseTypedEntityIds))
            {
                yield return nested;
            }
        }

        if (symbol is IArrayTypeSymbol arrayType)
        {
            foreach (var nested in EnumerateNamedTypes(arrayType.ElementType, traverseTypedEntityIds))
            {
                yield return nested;
            }
        }
    }

    private static bool TryResolveTargetOwner(INamedTypeSymbol symbol, out string ownerModule)
    {
        if (ArchitectureTestHelpers.TryGetPersistedEntityOwner(symbol, out ownerModule))
        {
            return true;
        }

        var metadataName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);

        if (RepositoryOwnerByMetadataName.TryGetValue(metadataName, out ownerModule!))
        {
            return true;
        }

        ownerModule = string.Empty;
        return false;
    }

    private static bool IsTypedEntityIdArgument(TypeSyntax typeSyntax, SemanticModel semanticModel)
    {
        if (typeSyntax.Ancestors().OfType<TypeArgumentListSyntax>().Any(typeArgumentList =>
            typeArgumentList.Parent is GenericNameSyntax { Identifier.ValueText: "Id" }))
        {
            return true;
        }

        return typeSyntax.Ancestors()
            .OfType<TypeArgumentListSyntax>()
            .Any(typeArgumentList =>
                typeArgumentList.Parent is GenericNameSyntax genericName &&
                semanticModel.GetTypeInfo(genericName).Type is INamedTypeSymbol type
                && IsTypedEntityId(type));
    }

    private static bool IsTypedEntityIdUsage(TypeSyntax typeSyntax, SemanticModel semanticModel) =>
        typeSyntax.AncestorsAndSelf().OfType<GenericNameSyntax>().Any(genericName =>
            semanticModel.GetTypeInfo(genericName).Type is INamedTypeSymbol namedType
            && IsTypedEntityId(namedType))
        || typeSyntax.AncestorsAndSelf().OfType<GenericNameSyntax>().Any(genericName =>
            genericName.Identifier.ValueText == "Id"
            && genericName.TypeArgumentList.Arguments.Count == 1)
        || IsTypedEntityIdRebindArgument(typeSyntax, semanticModel)
        || IsTypedEntityIdArgument(typeSyntax, semanticModel);

    private static bool IsTypedEntityIdRebindArgument(TypeSyntax typeSyntax, SemanticModel semanticModel)
    {
        foreach (var genericName in typeSyntax.AncestorsAndSelf()
                     .OfType<GenericNameSyntax>()
                     .Where(name => name.Identifier.ValueText == "Rebind"))
        {
            if (semanticModel.GetSymbolInfo(genericName).Symbol is IMethodSymbol { ReturnType: INamedTypeSymbol returnType }
                && IsTypedEntityId(returnType))
            {
                return true;
            }

            if (genericName.Parent is MemberAccessExpressionSyntax memberAccess
                && genericName.TypeArgumentList.Arguments.Any(argument => argument.Span.Contains(typeSyntax.Span))
                && (IsTypedEntityIdRebind(memberAccess, semanticModel)
                    || memberAccess.Name.Identifier.ValueText == "Rebind"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTypedEntityIdRebind(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel)
    {
        var receiverTypeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
        if ((receiverTypeInfo.Type is INamedTypeSymbol receiverType && IsTypedEntityId(receiverType))
            || (receiverTypeInfo.ConvertedType is INamedTypeSymbol convertedReceiverType && IsTypedEntityId(convertedReceiverType)))
        {
            return true;
        }

        var invocation = memberAccess.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation == null)
        {
            return false;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        return symbolInfo.Symbol is IMethodSymbol { ReturnType: INamedTypeSymbol returnType } && IsTypedEntityId(returnType)
            || symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().Any(method =>
                method.ReturnType is INamedTypeSymbol candidateReturnType && IsTypedEntityId(candidateReturnType));
    }

    private static bool IsPersistedForeignKeyConfigurationUsage(TypeSyntax typeSyntax, SemanticModel semanticModel)
    {
        foreach (var invocation in typeSyntax.AncestorsAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.ValueText is "HasOne" or "WithMany" or "HasForeignKey"
                && semanticModel.GetTypeInfo(memberAccess.Expression).Type is INamedTypeSymbol receiverType
                && receiverType.ContainingNamespace.ToDisplayString().StartsWith("Microsoft.EntityFrameworkCore.Metadata.Builders", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTypedEntityIdAliasDeclaration(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel,
        CompilationUnitSyntax root)
    {
        var usingDirective = typeSyntax.AncestorsAndSelf().OfType<UsingDirectiveSyntax>().FirstOrDefault();
        if (usingDirective?.Alias == null || semanticModel.GetDeclaredSymbol(usingDirective) is not IAliasSymbol aliasSymbol)
        {
            return false;
        }

        var aliasUsages = root
            .DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => SymbolEqualityComparer.Default.Equals(semanticModel.GetAliasInfo(identifier), aliasSymbol))
            .ToList();

        return aliasUsages.All(aliasUsage => IsTypedEntityIdUsage(aliasUsage, semanticModel));
    }

    private static bool IsTypedEntityId(INamedTypeSymbol type)
    {
        return type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::LgymApi.Domain.ValueObjects.Id<TEntity>";
    }

    private static string? GetEnclosingSourceSymbol(SemanticModel semanticModel, SyntaxNode node)
    {
        foreach (var current in node.AncestorsAndSelf())
        {
            ISymbol? symbol = current switch
            {
                MethodDeclarationSyntax method => semanticModel.GetDeclaredSymbol(method),
                ConstructorDeclarationSyntax constructor => semanticModel.GetDeclaredSymbol(constructor),
                PropertyDeclarationSyntax property => semanticModel.GetDeclaredSymbol(property),
                FieldDeclarationSyntax field when field.Declaration.Variables.FirstOrDefault() is { } variable => semanticModel.GetDeclaredSymbol(variable),
                EventDeclarationSyntax @event => semanticModel.GetDeclaredSymbol(@event),
                ClassDeclarationSyntax @class => semanticModel.GetDeclaredSymbol(@class),
                InterfaceDeclarationSyntax @interface => semanticModel.GetDeclaredSymbol(@interface),
                RecordDeclarationSyntax record => semanticModel.GetDeclaredSymbol(record),
                StructDeclarationSyntax @struct => semanticModel.GetDeclaredSymbol(@struct),
                _ => null
            };

            if (symbol != null)
            {
                return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
            }
        }

        return null;
    }

    private static string? TryGetApplicationModuleName(string relativePath)
    {
        var normalized = ArchitectureTestHelpers.NormalizePath(relativePath);

        return normalized switch
        {
            var path when path.StartsWith("LgymApi.TrainingPlanning/", StringComparison.OrdinalIgnoreCase)
                || RemainingTrainingPlanningApplicationAdapters.Contains(path, StringComparer.OrdinalIgnoreCase)
                => "Training Planning",
            var path when path.StartsWith("LgymApi.Application/User/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Role/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/ExternalAuth/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Features/Tutorial/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Features/PasswordReset/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Features/AdminManagement/", StringComparison.OrdinalIgnoreCase)
                => "Identity & Accounts",
            var path when path.StartsWith("LgymApi.Notifications/", StringComparison.OrdinalIgnoreCase)
                => "Notifications",
            var path when path.StartsWith("LgymApi.Application/Features/Reporting/", StringComparison.OrdinalIgnoreCase)
                => "Reporting",
            var path when path.StartsWith("LgymApi.Application/TrainingPlanning/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/PlanDay/", StringComparison.OrdinalIgnoreCase)
                => "Training Planning",
            var path when path.StartsWith("LgymApi.Application/WorkoutProgress/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Training/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/EloRegistry/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Exercise/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/ExerciseScores/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Gym/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Measurements/", StringComparison.OrdinalIgnoreCase)
                => "Workout & Progress",
            var path when path.StartsWith("LgymApi.Application/TrainerRelationships/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Features/TraineeNotes/", StringComparison.OrdinalIgnoreCase)
                => "Coaching",
            var path when path.StartsWith("LgymApi.Application/MainRecords/", StringComparison.OrdinalIgnoreCase)
                => "Workout & Progress",
            var path when path.StartsWith("LgymApi.Application/Features/DietPlans/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Features/Supplementation/", StringComparison.OrdinalIgnoreCase)
                => "Nutrition",
            _ => null
        };
    }

    private static bool IsTrainingPlanningBoundarySourcePath(string relativePath)
        => TryGetApplicationModuleName(relativePath) == ArchitectureTestHelpers.TrainingPlanningModuleName;

    private static string? TryGetTrainingPlanningModuleFromNamespace(CompilationUnitSyntax root)
    {
        var declaresTrainingPlanningNamespace = root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Select(declaration => declaration.Name.ToString())
            .Any(namespaceName =>
                namespaceName.StartsWith("LgymApi.Application.TrainingPlanning", StringComparison.Ordinal)
                || namespaceName.StartsWith("LgymApi.Application.Features.PlanDay", StringComparison.Ordinal));

        return declaresTrainingPlanningNamespace
            ? ArchitectureTestHelpers.TrainingPlanningModuleName
            : null;
    }

    private static bool IsBuildArtifactPath(string path)
    {
        var normalized = ArchitectureTestHelpers.NormalizePath(path);
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TrainingPlanningPublicSurfaceViolation(string Category, string Source, string Target)
    {
        public string Identity => $"{Category}|{Source}|{Target}";
    }
}
