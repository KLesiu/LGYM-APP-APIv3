using LgymApi.Application.Features.Measurements;
using LgymApi.Application.WorkoutProgress.Contracts.Measurements;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class MeasurementsCoachingAuthorizationBoundaryGuardTests
{
    [Test]
    public void MeasurementsRelationshipAccessPort_ShouldBeWorkoutProgressOwnedAndIdOnly()
    {
        var contract = typeof(IMeasurementsRelationshipAccessPort);
        var method = contract.GetMethods().Single();
        var parameters = method.GetParameters();

        Assert.Multiple(() =>
        {
            Assert.That(contract.Namespace, Is.EqualTo("LgymApi.Application.WorkoutProgress.Contracts.Measurements"));
            Assert.That(method.ReturnType, Is.EqualTo(typeof(Task<bool>)));
            Assert.That(parameters.Select(parameter => parameter.ParameterType), Is.EqualTo(new[]
            {
                typeof(Id<AccountReference>),
                typeof(Id<AccountReference>),
                typeof(CancellationToken)
            }));
        });
    }

    [Test]
    public void MeasurementsAuthorization_ShouldUseIdentityRoleFactsAndTheConsumerOwnedRelationshipPort()
    {
        var serviceDependencies = typeof(MeasurementsService).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(serviceDependencies, Is.EqualTo(new[]
            {
                typeof(IWorkoutProgressReadWriteService),
                typeof(IAccountAccessReader),
                typeof(IMeasurementsRelationshipAccessPort)
            }));
            Assert.That(serviceDependencies.Select(type => type.Name), Does.Not.Contain("IRoleRepository"));
            Assert.That(serviceDependencies.Select(type => type.Name), Does.Not.Contain("ITrainerRelationshipRepository"));
        });
    }

    [TestCase(
        "Fixture.WorkoutProgress.Contracts.Measurements.IMeasurementsRelationshipAccessPort",
        true,
        TestName = "Measurements_consumer_owned_port")]
    [TestCase(
        "Fixture.Coaching.Contracts.Access.ICoachingRelationshipAccessService",
        false,
        TestName = "Measurements_direct_Coaching_access")]
    [TestCase(
        "Fixture.Coaching.Persistence.ITrainerRelationshipRepository",
        false,
        TestName = "Measurements_direct_relationship_repository")]
    public void MeasurementsDependencyFixture_ShouldAllowOnlyConsumerOwnedPort(string dependencyType, bool isAllowed)
    {
        var violations = CollectMeasurementsDependencyViolations(dependencyType);

        Assert.That(violations, isAllowed ? Is.Empty : Has.Count.EqualTo(1));
        if (!isAllowed)
        {
            Assert.That(violations.Single(), Is.EqualTo(dependencyType));
        }
    }

    private static IReadOnlyList<string> CollectMeasurementsDependencyViolations(string dependencyType)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            namespace Fixture.WorkoutProgress.Contracts.Measurements
            {
                public interface IMeasurementsRelationshipAccessPort { }
            }

            namespace Fixture.Coaching.Contracts.Access
            {
                public interface ICoachingRelationshipAccessService { }
            }

            namespace Fixture.Coaching.Persistence
            {
                public interface ITrainerRelationshipRepository { }
            }

            namespace Fixture.WorkoutProgress.Measurements
            {
                public sealed class MeasurementsService
                {
                    public {{dependencyType}} Dependency { get; init; } = default!;
                }
            }
            """, path: "LgymApi.Application/Measurements/MeasurementsDependencyFixture.cs");
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);
        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "The compiler fixture must be valid before the Measurements dependency rule is evaluated.");

        var property = tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var targetType = compilation.GetSemanticModel(tree).GetTypeInfo(property.Type).Type;
        Assert.That(targetType, Is.Not.Null, "The compiler fixture must bind the dependency type.");

        return targetType!.ContainingNamespace.ToDisplayString().StartsWith("Fixture.Coaching", StringComparison.Ordinal)
            ? [targetType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)]
            : [];
    }
}
