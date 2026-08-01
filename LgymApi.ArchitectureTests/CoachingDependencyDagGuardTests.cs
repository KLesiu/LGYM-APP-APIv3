using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using LgymApi.Api.Features.Public.Controllers;
using LgymApi.Application.Coaching.Invitations.PublicStatus;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingDependencyDagGuardTests
{
    private const string RelationshipRepositoryName = "ITrainerRelationshipRepository";
    private const string RoleRepositoryName = "IRoleRepository";
    private const string RelationshipAccessContractName = "ICoachingRelationshipAccessService";

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedContractsBySource =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["Reporting"] = CreateSet("Fixture.Contracts.ICoachingRelationshipAccessService"),
            ["Nutrition"] = CreateSet("Fixture.Contracts.ICoachingRelationshipAccessService"),
            ["Training Planning"] = CreateSet("Fixture.Contracts.IPlanDayRelationshipAccessPort"),
            ["Workout & Progress"] = CreateSet("Fixture.Contracts.IMeasurementsRelationshipAccessPort"),
            ["Coaching"] = CreateSet("Fixture.Contracts.IGetManagedPlansUseCase"),
            ["API"] = CreateSet("Fixture.Contracts.ICoachingPublicReadService"),
            ["Worker"] = CreateSet("Fixture.Contracts.ICoachingNotificationIntentService")
        };

    private static readonly IReadOnlySet<string> ExpectedProductionConsumers = CreateSet();

    [TestCaseSource(nameof(TargetDagFixtures))]
    public void Target_Dependency_Dag_Should_Reject_Forbidden_And_Allow_Named_Contracts(object fixtureValue)
    {
        var fixture = (DagFixture)fixtureValue;
        var violations = CollectDagViolations(fixture);

        Assert.That(violations, fixture.IsAllowed ? Is.Empty : Has.Count.EqualTo(1));

        if (!fixture.IsAllowed)
        {
            Assert.That(violations.Single(), Is.EqualTo(new DagViolation(fixture.SourceModule, fixture.TargetContract)));
        }
    }

    [Test]
    public void Current_Relationship_Repository_Consumers_Should_Match_Explicit_Production_Inventory()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var consumers = CollectRelationshipRepositoryConsumers(repoRoot);

        Assert.That(consumers, Is.EquivalentTo(ExpectedProductionConsumers));
    }

    [Test]
    public void PublicInvitationController_Constructor_Should_Use_Only_Focused_Status_UseCase_And_Mapper()
    {
        var parameters = typeof(PublicInvitationController).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.That(parameters, Is.EqualTo(new[] { typeof(IPublicInvitationStatusUseCase), typeof(IMapper) }));
    }

    [Test]
    public void Test_Relationship_Repository_Implementations_Should_Match_Explicit_Inventory()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var substitutes = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.UnitTests")
            .Select(path => (Path: path, Tree: CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)))
            .SelectMany(entry => entry.Tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(@class => @class.BaseList?.Types.Any(baseType => baseType.Type is IdentifierNameSyntax
                {
                    Identifier.ValueText: RelationshipRepositoryName
                }) == true)
                .Select(@class => $"{ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, entry.Path))} @ {@class.Identifier.ValueText}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

        Assert.That(substitutes, Is.Empty);
    }

    private static IEnumerable<TestCaseData> TargetDagFixtures()
    {
        yield return Case("Reporting_public_access", "Reporting", "ICoachingRelationshipAccessService", true);
        yield return Case("Nutrition_public_access", "Nutrition", "ICoachingRelationshipAccessService", true);
        yield return Case("Nutrition_repository", "Nutrition", RelationshipRepositoryName, false);
        yield return Case("Nutrition_persistence", "Nutrition", "ICoachingActiveLinkPersistence", false);
        yield return Case("Nutrition_private_service", "Nutrition", "CoachingRelationshipAccessService", false);
        yield return Case("Reporting_repository", "Reporting", RelationshipRepositoryName, false);
        yield return Case("Reporting_service", "Reporting", "TrainerRelationshipService", false);
        yield return Case("Nutrition_entity", "Nutrition", "TrainerTraineeLink", false);
        yield return Case("Reporting_dependency_bag", "Reporting", "CoachingDependencyBag", false);
        yield return Case("Training_Planning_consumer_port", "Training Planning", "IPlanDayRelationshipAccessPort", true);
        yield return Case("Training_Planning_direct_access", "Training Planning", "ICoachingRelationshipAccessService", false);
        yield return Case("Training_Planning_repository", "Training Planning", RelationshipRepositoryName, false);
        yield return Case("Workout_consumer_port", "Workout & Progress", "IMeasurementsRelationshipAccessPort", true);
        yield return Case("Workout_direct_access", "Workout & Progress", "ICoachingRelationshipAccessService", false);
        yield return Case("Workout_entity", "Workout & Progress", "TrainerTraineeLink", false);
        yield return Case("Coaching_planning_managed_plan_contract", "Coaching", "IGetManagedPlansUseCase", true);
        yield return Case("Coaching_planning_repository", "Coaching", "IPlanRepository", false);
        yield return Case("Coaching_planning_entity", "Coaching", "Plan", false);
        yield return Case("Coaching_planning_private_service", "Coaching", "GetManagedPlansUseCase", false);
        yield return Case("API_public_read", "API", "ICoachingPublicReadService", true);
        yield return Case("API_repository", "API", RelationshipRepositoryName, false);
        yield return Case("Worker_public_intent", "Worker", "ICoachingNotificationIntentService", true);
        yield return Case("Worker_repository", "Worker", RelationshipRepositoryName, false);
        yield return Case("Platform_feature_dump", "Platform / Reference Data", "ICoachingRelationshipAccessService", false);
        yield return Case("Coaching_reporting_cycle", "Coaching", "IReportingInternalService", false);
        yield return Case("Coaching_nutrition_cycle", "Coaching", "INutritionInternalService", false);
    }

    private static TestCaseData Case(string name, string sourceModule, string targetContract, bool isAllowed)
    {
        return new TestCaseData(new DagFixture(name, sourceModule, targetContract, isAllowed)).SetName(name);
    }

    private static IReadOnlyList<DagViolation> CollectDagViolations(DagFixture fixture)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using Fixture.Contracts;

            namespace Fixture.Contracts
            {
                public interface ICoachingRelationshipAccessService { }
                public interface ICoachingActiveLinkPersistence { }
                public interface ITrainerRelationshipRepository { }
                public interface IPlanDayRelationshipAccessPort { }
                public interface IMeasurementsRelationshipAccessPort { }
                public interface IGetManagedPlansUseCase { }
                public interface IPlanRepository { }
                public interface ICoachingPublicReadService { }
                public interface ICoachingNotificationIntentService { }
                public interface IReportingInternalService { }
                public interface INutritionInternalService { }
                public sealed class CoachingRelationshipAccessService { }
                public sealed class TrainerRelationshipService { }
                public sealed class TrainerTraineeLink { }
                public sealed class Plan { }
                internal sealed class GetManagedPlansUseCase { }
                public sealed class CoachingDependencyBag { }
            }

            namespace Fixture.Consumer
            {
                public sealed class Consumer
                {
                    internal {{fixture.TargetContract}} Dependency { get; init; } = default!;
                }
            }
            """, path: $"Fixtures/{fixture.Name}.cs");
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);
        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "The compiler fixture must be valid before the dependency rule is evaluated.");
        var semanticModel = compilation.GetSemanticModel(tree);
        var dependencyType = tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(property => property.Identifier.ValueText == "Dependency").Type;
        var targetContract = semanticModel.GetTypeInfo(dependencyType).Type?
            .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        Assert.That(targetContract, Is.Not.Null, "The compiler fixture must bind its target contract.");

        return IsAllowedContract(fixture.SourceModule, targetContract!)
            ? []
            : [new DagViolation(fixture.SourceModule, fixture.TargetContract)];
    }

    private static IReadOnlyList<string> CollectRelationshipRepositoryConsumers(string repoRoot)
    {
        return ArchitectureTestHelpers.EnumerateProductionSourceFiles("LgymApi.Application", "LgymApi.Api", "LgymApi.BackgroundWorker")
            .Select(path => (Path: path, Tree: CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)))
            .SelectMany(entry => entry.Tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => identifier.Identifier.ValueText == RelationshipRepositoryName)
                .Select(identifier => DescribeConsumer(entry.Path, identifier, repoRoot)))
            .Where(description => description is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(description => description, StringComparer.Ordinal)
            .ToList();
    }

    private static string? DescribeConsumer(string path, SyntaxNode reference, string repoRoot)
    {
        var containingType = reference.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault();
        var containingNamespace = reference.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

        return containingType is null || containingNamespace is null
            ? null
            : $"{containingNamespace.Name}.{containingType.Identifier.ValueText} @ {ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, path))}";
    }

    private static bool IsAllowedContract(string sourceModule, string targetContract)
    {
        return AllowedContractsBySource.TryGetValue(sourceModule, out var allowedContracts)
            && allowedContracts.Contains(targetContract);
    }

    private static IReadOnlySet<string> CreateSet(params string[] values)
    {
        return new HashSet<string>(values, StringComparer.Ordinal);
    }

    private sealed record DagFixture(string Name, string SourceModule, string TargetContract, bool IsAllowed);

    private sealed record DagViolation(string SourceModule, string TargetContract);
}
