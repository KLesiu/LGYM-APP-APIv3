using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class WorkoutProgressPublicContractGuardTests
{
    private const string TypedIdMetadataName = "LgymApi.Domain.ValueObjects.Id`1";

    private static readonly IReadOnlySet<string> ForeignModuleNames = new HashSet<string>(StringComparer.Ordinal)
    {
        ArchitectureTestHelpers.IdentityModuleName,
        ArchitectureTestHelpers.CoachingModuleName,
        ArchitectureTestHelpers.NutritionModuleName,
        ArchitectureTestHelpers.NotificationsModuleName
    };

    private static readonly IReadOnlySet<string> AllowedPublicContractMetadataNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "LgymApi.Application.WorkoutProgress.ProgressData.IWorkoutProgressReadWriteService",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.ExerciseScoreChartPoint",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.MeasurementReadModel",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.MeasurementTrendReadModel",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordReadModel",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.ProgressExerciseReadModel",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordBestReadModel",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.PossibleRecordReadModel",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.EloChartPoint",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.MeasurementWriteModel",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordCreateWriteModel",
        "LgymApi.Application.WorkoutProgress.ProgressData.Models.MainRecordUpdateWriteModel",
        "LgymApi.Application.WorkoutProgress.Dashboard.IWorkoutProgressDashboardReadService",
        "LgymApi.Application.WorkoutProgress.Dashboard.Models.WorkoutProgressDashboardTrainingReadModel",
        "LgymApi.Application.WorkoutProgress.Dashboard.Models.WorkoutProgressDashboardPlanDayReadModel",
        "LgymApi.Application.WorkoutProgress.Dashboard.Models.WorkoutProgressDashboardExerciseReadModel",
        "LgymApi.Application.WorkoutProgress.Dashboard.Models.WorkoutProgressDashboardExerciseDetailsReadModel",
        "LgymApi.Application.WorkoutProgress.Dashboard.Models.WorkoutProgressDashboardExerciseScoreReadModel",
        "LgymApi.Application.WorkoutProgress.Ranking.IWorkoutProgressRankingReadService",
        "LgymApi.Application.WorkoutProgress.Ranking.Models.RankingReadModel",
        "LgymApi.Application.WorkoutProgress.TrainingExecution.ICompleteTrainingUseCase",
        "LgymApi.Application.WorkoutProgress.TrainingExecution.CompleteTrainingInput",
        "LgymApi.Application.WorkoutProgress.TrainingExecution.ITrainingHistoryReadService",
        "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.IReportSubmissionAcceptedProgressConsumer",
        "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedProgressEvent",
        "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedMeasurement",
        "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedProgressValidationResult",
        "LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration.ReportSubmissionAcceptedProgressConsumeResult",
        "LgymApi.Application.WorkoutProgress.Contracts.Measurements.IMeasurementsRelationshipAccessPort"
    };

    [Test]
    public void Foreign_Modules_Should_Use_Only_Explicit_Workout_Progress_Contracts_And_Read_Models()
    {
        var (_, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application");

        Assert.That(
            CollectViolations(compilation, syntaxTrees),
            Is.Empty,
            "Foreign modules must consume Workout & Progress only through its explicit contracts and read models.");
    }

    [Test]
    public void Direct_Workout_Progress_Entities_And_Repositories_Should_Be_Rejected()
    {
        var (repoRoot, compilation, _) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application");
        var fixture = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("""
            using LgymApi.Application.Repositories;
            using LgymApi.Domain.Entities;

            namespace LgymApi.Application.TrainerRelationships;

            public sealed class ForeignWorkoutProgressPersistenceAccess
            {
                public IMainRecordRepository MainRecordRepository { get; init; } = default!;
                public IEloRegistryRepository EloRegistryRepository { get; init; } = default!;
                public IExerciseScoreRepository ExerciseScoreRepository { get; init; } = default!;
                public ITrainingRepository TrainingRepository { get; init; } = default!;
                public MainRecord MainRecord { get; init; } = default!;
                public EloRegistry EloRegistry { get; init; } = default!;
                public ExerciseScore ExerciseScore { get; init; } = default!;
                public Training Training { get; init; } = default!;
            }
            """, path: Path.Combine(repoRoot, "LgymApi.Application", "TrainerRelationships", "ForeignWorkoutProgressPersistenceAccess.cs"));

        var violations = CollectViolations(compilation.AddSyntaxTrees(fixture), [fixture]);

        Assert.That(
            violations.Select(violation => violation.TargetMetadataName),
            Is.EquivalentTo(new[]
            {
                "LgymApi.Application.Repositories.IMainRecordRepository",
                "LgymApi.Application.Repositories.IEloRegistryRepository",
                "LgymApi.Application.Repositories.IExerciseScoreRepository",
                "LgymApi.Application.Repositories.ITrainingRepository",
                "LgymApi.Domain.Entities.MainRecord",
                "LgymApi.Domain.Entities.EloRegistry",
                "LgymApi.Domain.Entities.ExerciseScore",
                "LgymApi.Domain.Entities.Training"
            }));
    }

    [Test]
    public void Exercise_Typed_Ids_Should_Be_Allowed_As_Identifier_Only_Transport()
    {
        var (repoRoot, compilation, _) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application");
        var fixture = CSharpSyntaxTree.ParseText("""
            using LgymApi.Domain.ValueObjects;
            using ExerciseEntity = LgymApi.Domain.Entities.Exercise;

            namespace LgymApi.Application.Coaching.Progress;

            public sealed record IdentifierOnlyExerciseQuery(
                Id<ExerciseEntity> ExerciseId,
                Id<LgymApi.Domain.Entities.Exercise> FullyQualifiedExerciseId);
            """, path: Path.Combine(repoRoot, "LgymApi.Application", "Coaching", "Progress", "IdentifierOnlyExerciseQuery.cs"));

        CollectViolations(compilation.AddSyntaxTrees(fixture), [fixture]).Should().BeEmpty();
    }

    [TestCase("", "Exercise DirectExercise { get; init; } = default!;", "LgymApi.Domain.Entities.Exercise")]
    [TestCase("using ExerciseEntity = LgymApi.Domain.Entities.Exercise;", "ExerciseEntity DirectAliasedExercise { get; init; } = default!;", "LgymApi.Domain.Entities.Exercise")]
    [TestCase("", "Exercise[] ExerciseCollection { get; init; } = [];", "LgymApi.Domain.Entities.Exercise")]
    [TestCase("", "IReadOnlyCollection<Exercise> ExerciseCollection { get; init; } = [];", "LgymApi.Domain.Entities.Exercise")]
    [TestCase("", "ForeignWrapper<Exercise> ExerciseWrapper { get; init; } = new();", "LgymApi.Domain.Entities.Exercise")]
    [TestCase("", "IExerciseRepository ExerciseRepository { get; init; } = default!;", "LgymApi.Application.Repositories.IExerciseRepository")]
    public void Direct_Exercise_Shapes_And_Repository_Should_Be_Rejected(
        string aliasDirective,
        string declaration,
        string expectedTarget)
    {
        var (repoRoot, compilation, _) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application");
        var fixture = CSharpSyntaxTree.ParseText($$"""
            using System.Collections.Generic;
            using LgymApi.Application.Repositories;
            using LgymApi.Domain.Entities;
            {{aliasDirective}}

            namespace LgymApi.Application.Coaching.Progress;

            public sealed class ForeignWrapper<T> { }
            public sealed class ForeignExerciseExposure
            {
                public {{declaration}}
            }
            """, path: Path.Combine(repoRoot, "LgymApi.Application", "Coaching", "Progress", "ForeignExerciseExposure.cs"));

        var violations = CollectViolations(compilation.AddSyntaxTrees(fixture), [fixture]);

        violations.Should().NotBeEmpty();
        violations.Select(violation => violation.TargetMetadataName).Should().Contain(expectedTarget);
    }

    private static IReadOnlyList<Violation> CollectViolations(CSharpCompilation compilation, IEnumerable<SyntaxTree> syntaxTrees)
    {
        var violations = new Dictionary<string, Violation>(StringComparer.Ordinal);
        var typedIdDefinition = compilation.GetTypeByMetadataName(TypedIdMetadataName)
            ?? throw new InvalidOperationException($"Unable to resolve '{TypedIdMetadataName}'.");

        foreach (var tree in syntaxTrees)
        {
            var sourceModule = ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(tree.FilePath);
            if (sourceModule is null || !ForeignModuleNames.Contains(sourceModule))
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();
            foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
            {
                if (IsIdentifierOnlyTypedIdReference(typeSyntax, semanticModel, typedIdDefinition)
                    || IsIdentifierOnlyAliasDeclaration(typeSyntax, semanticModel, root, typedIdDefinition))
                {
                    continue;
                }

                var type = ArchitectureTestHelpers.GetOwnedNamedTypeSymbol(semanticModel.GetTypeInfo(typeSyntax).Type);
                if (type is null || !IsWorkoutProgressType(type))
                {
                    continue;
                }

                var targetMetadataName = type.OriginalDefinition
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty, StringComparison.Ordinal);
                if (AllowedPublicContractMetadataNames.Contains(targetMetadataName))
                {
                    continue;
                }

                if (ArchitectureTestHelpers.MatchesApiAdapterDependencyContract(tree.FilePath, targetMetadataName))
                {
                    continue;
                }

                var violation = new Violation(sourceModule, targetMetadataName, tree.FilePath, typeSyntax.ToString());
                violations.TryAdd(violation.Identity, violation);
            }
        }

        return violations.Values.OrderBy(violation => violation.Identity, StringComparer.Ordinal).ToList();
    }

    private static bool IsIdentifierOnlyTypedIdReference(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel,
        INamedTypeSymbol typedIdDefinition)
    {
        var referencedType = semanticModel.GetTypeInfo(typeSyntax).Type;
        if (referencedType is null)
        {
            return false;
        }

        return typeSyntax.Ancestors().OfType<TypeArgumentListSyntax>().Any(typeArguments =>
            typeArguments.Parent is GenericNameSyntax genericName
            && semanticModel.GetTypeInfo(genericName).Type is INamedTypeSymbol constructedType
            && SymbolEqualityComparer.Default.Equals(constructedType.OriginalDefinition, typedIdDefinition)
            && constructedType.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(constructedType.TypeArguments[0], referencedType));
    }

    private static bool IsIdentifierOnlyAliasDeclaration(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel,
        CompilationUnitSyntax root,
        INamedTypeSymbol typedIdDefinition)
    {
        var usingDirective = typeSyntax.AncestorsAndSelf().OfType<UsingDirectiveSyntax>().FirstOrDefault();
        if (usingDirective?.Alias is null
            || semanticModel.GetDeclaredSymbol(usingDirective) is not IAliasSymbol aliasSymbol)
        {
            return false;
        }

        var aliasUsages = root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(identifier => SymbolEqualityComparer.Default.Equals(semanticModel.GetAliasInfo(identifier), aliasSymbol))
            .ToArray();
        return aliasUsages.Length > 0
            && aliasUsages.All(aliasUsage => IsIdentifierOnlyTypedIdReference(aliasUsage, semanticModel, typedIdDefinition));
    }

    private static bool IsWorkoutProgressType(INamedTypeSymbol type)
    {
        if (ArchitectureTestHelpers.GetCanonicalModuleNameForSymbol(type) == ArchitectureTestHelpers.WorkoutProgressModuleName)
        {
            return true;
        }

        return ArchitectureTestHelpers.TryGetPersistedEntityOwner(type, out var owner) &&
               owner == ArchitectureTestHelpers.WorkoutProgressModuleName;
    }

    private sealed record Violation(string SourceModule, string TargetMetadataName, string SourceFile, string SourceType)
    {
        public string Identity => $"{SourceModule}|{SourceFile}|{SourceType}|{TargetMetadataName}";

        public override string ToString() => $"{SourceModule}: {SourceType} references {TargetMetadataName} in {SourceFile}";
    }
}
