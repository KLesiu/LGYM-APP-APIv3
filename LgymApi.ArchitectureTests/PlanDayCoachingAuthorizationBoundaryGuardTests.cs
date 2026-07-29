using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class PlanDayCoachingAuthorizationBoundaryGuardTests
{
    [Test]
    public void ProductionPlanDayDependencies_ShouldUseOnlyConsumerOwnedRelationshipPort()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var planDaySources = new[]
        {
            Path.Combine(repositoryRoot, "LgymApi.TrainingPlanning", "Contracts", "PlanDay", "IPlanDayRelationshipAccessPort.cs"),
            Path.Combine(repositoryRoot, "LgymApi.TrainingPlanning", "PlanDay", "PlanDayService.cs")
        }.Select(File.ReadAllText).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(planDaySources, Has.Some.Contains("private readonly IPlanDayRelationshipAccessPort _relationshipAccess"));
            Assert.That(planDaySources, Has.All.Not.Contains("ITrainerRelationshipRepository"));
            Assert.That(planDaySources, Has.All.Not.Contains("LgymApi.Application.Coaching"));
        });
    }

    [Test]
    public void PlanDayRelationshipAccessPort_ShouldBeTrainingPlanningOwnedAndIdOnly()
    {
        var contract = typeof(IPlanDayRelationshipAccessPort);
        var method = contract.GetMethods().Single();
        var parameters = method.GetParameters();

        Assert.Multiple(() =>
        {
            Assert.That(contract.Namespace, Is.EqualTo("LgymApi.Application.TrainingPlanning.Contracts.PlanDay"));
            Assert.That(method.ReturnType, Is.EqualTo(typeof(Task<bool>)));
            Assert.That(parameters.Select(parameter => parameter.ParameterType), Is.EqualTo(new[]
            {
                typeof(Id<AccountReference>),
                typeof(Id<AccountReference>),
                typeof(CancellationToken)
            }));
        });
    }

    [TestCase(
        "Fixture.TrainingPlanning.Contracts.PlanDay.IPlanDayRelationshipAccessPort",
        true,
        TestName = "PlanDay_consumer_owned_port")]
    [TestCase(
        "Fixture.Coaching.Contracts.Access.ICoachingRelationshipAccessService",
        false,
        TestName = "PlanDay_direct_Coaching_access")]
    [TestCase(
        "Fixture.Coaching.Persistence.ITrainerRelationshipRepository",
        false,
        TestName = "PlanDay_direct_relationship_repository")]
    public void PlanDayDependencyFixture_ShouldAllowOnlyConsumerOwnedPort(string dependencyType, bool isAllowed)
    {
        var violations = CollectPlanDayDependencyViolations(dependencyType);

        Assert.That(violations, isAllowed ? Is.Empty : Has.Count.EqualTo(1));
        if (!isAllowed)
        {
            Assert.That(violations.Single(), Is.EqualTo(dependencyType));
        }
    }

    private static IReadOnlyList<string> CollectPlanDayDependencyViolations(string dependencyType)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            namespace Fixture.TrainingPlanning.Contracts.PlanDay
            {
                public interface IPlanDayRelationshipAccessPort { }
            }

            namespace Fixture.Coaching.Contracts.Access
            {
                public interface ICoachingRelationshipAccessService { }
            }

            namespace Fixture.Coaching.Persistence
            {
                public interface ITrainerRelationshipRepository { }
            }

            namespace Fixture.TrainingPlanning.PlanDay
            {
                public sealed class PlanDayService
                {
                    public {{dependencyType}} Dependency { get; init; } = default!;
                }
            }
            """, path: "LgymApi.TrainingPlanning/PlanDay/PlanDayDependencyFixture.cs");
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);
        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "The compiler fixture must be valid before the PlanDay dependency rule is evaluated.");

        var property = tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var targetType = compilation.GetSemanticModel(tree).GetTypeInfo(property.Type).Type;
        Assert.That(targetType, Is.Not.Null, "The compiler fixture must bind the dependency type.");

        return targetType!.ContainingNamespace.ToDisplayString().StartsWith("Fixture.Coaching", StringComparison.Ordinal)
            ? [targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)]
            : [];
    }
}
