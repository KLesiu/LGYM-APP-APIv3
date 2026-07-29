using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ReportingRelationshipAccessBoundaryGuardTests
{
    private static readonly string[] AffectedTestFactories =
    [
        "LgymApi.UnitTests/PhotoServiceTestFactory.cs",
        "LgymApi.UnitTests/RecurringReportAssignmentServiceRelationalTests.cs",
        "LgymApi.UnitTests/RecurringReportAssignmentServiceTests.cs",
        "LgymApi.UnitTests/ReportingServiceAcceptedProgressOutboxTests.cs",
        "LgymApi.UnitTests/ReportingServiceTests.cs"
    ];

    [Test]
    public void ReportingRelationshipAccessPort_ShouldRemainMarkerIdOnly()
    {
        var method = typeof(IReportingRelationshipAccessPersistence).GetMethods().Single();

        Assert.Multiple(() =>
        {
            Assert.That(method.ReturnType, Is.EqualTo(typeof(Task<ReportingRelationshipAccessFact>)));
            Assert.That(method.GetParameters().Select(parameter => parameter.ParameterType), Is.EqualTo(new[]
            {
                typeof(Id<AccountReference>),
                typeof(Id<AccountReference>),
                typeof(CancellationToken)
            }));
            Assert.That(typeof(ReportingRelationshipAccessFact).GetProperties().Select(property => property.PropertyType),
                Is.EqualTo(new[] { typeof(bool) }));
        });
    }

    [TestCase(typeof(IReportingServiceDependencies))]
    [TestCase(typeof(IRecurringReportAssignmentServiceDependencies))]
    public void ReportingDependencyBag_ShouldExposeOnlyConsumerOwnedRelationshipAccess(Type dependencyBag)
    {
        var relationshipDependencies = dependencyBag.GetProperties()
            .Select(property => property.PropertyType)
            .Where(type => type.Name.Contains("RelationshipAccess", StringComparison.Ordinal))
            .ToArray();
        var coachingDependencies = dependencyBag.GetProperties()
            .Select(property => property.PropertyType)
            .Where(type => type.Namespace?.StartsWith("LgymApi.Application.Coaching", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(relationshipDependencies, Is.EqualTo(new[] { typeof(IReportingRelationshipAccessPersistence) }));
            Assert.That(coachingDependencies, Is.Empty);
        });
    }

    [Test]
    public void ReportingProduction_ShouldNotReferenceCoachingAuthorizationImplementations()
    {
        var (repoRoot, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application");
        var reportingTrees = syntaxTrees
            .Where(tree => ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, tree.FilePath))
                .StartsWith("LgymApi.Application/Features/Reporting/", StringComparison.Ordinal))
            .ToArray();

        var coachingDependencies = reportingTrees
            .SelectMany(tree =>
            {
                var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
                return tree.GetRoot().DescendantNodes().OfType<TypeSyntax>()
                    .Select(type => semanticModel.GetTypeInfo(type).Type)
                    .Where(type => type?.ContainingNamespace.ToDisplayString()
                        .StartsWith("LgymApi.Application.Coaching", StringComparison.Ordinal) == true)
                    .Select(type => type!.ToDisplayString());
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.That(coachingDependencies, Is.Empty);
    }

    [Test]
    public void AffectedReportingTestFactories_ShouldUseConsumerOwnedAccessSubstitutes()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var invalidFactories = new List<string>();

        foreach (var relativePath in AffectedTestFactories)
        {
            var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var identifiers = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)
                .GetRoot()
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Select(identifier => identifier.Identifier.ValueText)
                .ToHashSet(StringComparer.Ordinal);

            if (!identifiers.Contains(nameof(IReportingRelationshipAccessPersistence))
                || identifiers.Contains("ICoachingRelationshipAccessService")
                || identifiers.Contains("ITrainerRelationshipRepository"))
            {
                invalidFactories.Add(relativePath);
            }
        }

        Assert.That(invalidFactories, Is.Empty);
    }
}
