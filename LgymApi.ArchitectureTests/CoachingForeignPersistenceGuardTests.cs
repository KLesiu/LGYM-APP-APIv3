using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CoachingForeignPersistenceGuardTests
{
    private const string ForeignAppDbContextType = "Fixture.Foreign.AppDbContext";
    private const string ForeignUserType = "Fixture.Foreign.User";
    private const string ForeignPlanType = "Fixture.Foreign.Plan";

    private static readonly IReadOnlySet<string> ForeignPersistenceTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Fixture.Foreign.IUserRepository",
        "Fixture.Foreign.IRoleRepository",
        "Fixture.Foreign.IPlanRepository",
        "Fixture.Foreign.User",
        "Fixture.Foreign.Plan"
    };

    [TestCaseSource(nameof(ForeignPersistenceFixtures))]
    public void Foreign_Persistence_And_Entity_Fixtures_Should_Reject_Direct_Values_And_Allow_Typed_Ids(object fixtureValue)
    {
        var fixture = (ForeignPersistenceFixture)fixtureValue;
        var violations = CollectForeignTypeViolations(fixture.Declaration);

        Assert.That(violations, fixture.IsAllowed ? Is.Empty : Has.Count.EqualTo(1));

        if (!fixture.IsAllowed)
        {
            Assert.That(violations.Single(), Is.EqualTo(fixture.TargetType));
        }
    }

    [TestCaseSource(nameof(UserQueryFixtures))]
    public void Coaching_Repository_Fixtures_Should_Reject_Users_Queries(object fixtureValue)
    {
        var fixture = (UserQueryFixture)fixtureValue;
        var violations = CollectUserQueryViolations(fixture.Query);

        Assert.That(violations, fixture.ExpectedViolation is null
            ? Is.Empty
            : Is.EqualTo(new[] { fixture.ExpectedViolation }));
    }

    private static IEnumerable<TestCaseData> ForeignPersistenceFixtures()
    {
        yield return ForeignCase("User_repository", "IUserRepository", false, "Fixture.Foreign.IUserRepository");
        yield return ForeignCase("Role_repository", "IRoleRepository", false, "Fixture.Foreign.IRoleRepository");
        yield return ForeignCase("Plan_repository", "IPlanRepository", false, "Fixture.Foreign.IPlanRepository");
        yield return ForeignCase("User_entity", "User", false, "Fixture.Foreign.User");
        yield return ForeignCase("Plan_entity", "Plan", false, "Fixture.Foreign.Plan");
        yield return ForeignCase("User_identifier", "Id<User>", true, "Fixture.Foreign.User");
        yield return ForeignCase("Plan_identifier", "Id<Plan>", true, "Fixture.Foreign.Plan");
    }

    private static IEnumerable<TestCaseData> UserQueryFixtures()
    {
        yield return QueryCase("Users_property", "foreignContext.Users", $"{ForeignAppDbContextType}.Users");
        yield return QueryCase("Users_set", "foreignContext.Set<User>()", $"{ForeignAppDbContextType}.Set<{ForeignUserType}>()");
        yield return QueryCase("Coaching_property", "foreignContext.TrainerInvitations");
        yield return QueryCase("Coaching_set", "foreignContext.Set<TrainerInvitation>()");
        yield return QueryCase("Plan_property", "foreignContext.Plans", $"{ForeignAppDbContextType}.Plans");
        yield return QueryCase("Plan_set", "foreignContext.Set<Plan>()", $"{ForeignAppDbContextType}.Set<{ForeignPlanType}>()");
        yield return QueryCase("Unrelated_Users_property", "unrelatedContext.Users");
        yield return QueryCase("Unrelated_Users_set", "unrelatedContext.Set<Fixture.Unrelated.User>()");
        yield return QueryCase("Unrelated_Plan_property", "unrelatedContext.Plans");
        yield return QueryCase("Unrelated_Plan_set", "unrelatedContext.Set<Fixture.Unrelated.Plan>()");
        yield return QueryCase("Foreign_context_unrelated_User_set", "foreignContext.Set<Fixture.Unrelated.User>()");
        yield return QueryCase("Unrelated_context_foreign_User_set", "unrelatedContext.Set<Fixture.Foreign.User>()");
    }

    private static TestCaseData ForeignCase(string name, string declaration, bool isAllowed, string targetType)
    {
        return new TestCaseData(new ForeignPersistenceFixture(name, declaration, isAllowed, targetType)).SetName(name);
    }

    private static TestCaseData QueryCase(string name, string query, string? expectedViolation = null)
    {
        return new TestCaseData(new UserQueryFixture(name, query, expectedViolation)).SetName(name);
    }

    private static IReadOnlyList<string> CollectForeignTypeViolations(string declaration)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            namespace Fixture.Foreign
            {
                public sealed class User { }
                public sealed class Plan { }
                public interface IUserRepository { }
                public interface IRoleRepository { }
                public interface IPlanRepository { }
                public readonly record struct Id<T>;
            }

            namespace Fixture.Coaching
            {
                using Fixture.Foreign;

                public sealed class LegacyCoachingConsumer
                {
                    public {{declaration}} Dependency { get; init; } = default!;
                }
            }
            """);
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);
        var semanticModel = compilation.GetSemanticModel(tree);

        return tree.GetRoot().DescendantNodes().OfType<TypeSyntax>()
            .Where(typeSyntax => !IsTypedEntityIdArgument(typeSyntax, semanticModel))
            .Select(typeSyntax => semanticModel.GetTypeInfo(typeSyntax).Type?
                .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .Where(typeName => typeName is not null && ForeignPersistenceTypes.Contains(typeName))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> CollectUserQueryViolations(string query)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using System.Collections.Generic;

            namespace Fixture.Foreign
            {
                public sealed class User { }
                public sealed class Plan { }
                public sealed class TrainerInvitation { }

                public sealed class AppDbContext
                {
                    public IReadOnlyList<User> Users { get; } = [];
                    public IReadOnlyList<Plan> Plans { get; } = [];
                    public IReadOnlyList<TrainerInvitation> TrainerInvitations { get; } = [];
                    public IReadOnlyList<T> Set<T>() => [];
                }

                public sealed class CoachingRepository
                {
                    public object Query(AppDbContext foreignContext, Fixture.Unrelated.AppDbContext unrelatedContext) => {{query}};
                }
            }

            namespace Fixture.Unrelated
            {
                public sealed class User { }
                public sealed class Plan { }

                public sealed class AppDbContext
                {
                    public IReadOnlyList<User> Users { get; } = [];
                    public IReadOnlyList<Plan> Plans { get; } = [];
                    public IReadOnlyList<T> Set<T>() => [];
                }
            }
            """);
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);
        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "The compiler fixture must be valid before the persistence rule is evaluated.");
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var foreignDbContext = compilation.GetTypeByMetadataName(ForeignAppDbContextType)
            ?? throw new AssertionException($"The compiler fixture must bind '{ForeignAppDbContextType}'.");
        var foreignEntityTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default)
        {
            compilation.GetTypeByMetadataName(ForeignUserType)
                ?? throw new AssertionException($"The compiler fixture must bind '{ForeignUserType}'."),
            compilation.GetTypeByMetadataName(ForeignPlanType)
                ?? throw new AssertionException($"The compiler fixture must bind '{ForeignPlanType}'.")
        };
        var forbiddenProperties = foreignDbContext.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(property => property.Name is "Users" or "Plans")
            .ToHashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
        var foreignSetMethod = foreignDbContext.GetMembers("Set")
            .OfType<IMethodSymbol>()
            .Single(method => method.Arity == 1 && method.Parameters.Length == 0);

        var propertyQueries = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Select(access => semanticModel.GetSymbolInfo(access).Symbol)
            .OfType<IPropertySymbol>()
            .Where(forbiddenProperties.Contains)
            .Select(property => $"{property.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}.{property.Name}");
        var setQueries = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(invocation => semanticModel.GetSymbolInfo(invocation).Symbol)
            .OfType<IMethodSymbol>()
            .Where(method => SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, foreignSetMethod)
                && method.TypeArguments.SingleOrDefault() is INamedTypeSymbol entityType
                && foreignEntityTypes.Contains(entityType))
            .Select(method => $"{method.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}.{method.Name}<{method.TypeArguments.Single().ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}>()");

        return propertyQueries.Concat(setQueries).Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool IsTypedEntityIdArgument(TypeSyntax typeSyntax, SemanticModel semanticModel)
    {
        return typeSyntax.Ancestors().OfType<TypeArgumentListSyntax>().Any(typeArgumentList =>
            typeArgumentList.Parent is GenericNameSyntax genericName
            && semanticModel.GetTypeInfo(genericName).Type is INamedTypeSymbol namedType
            && namedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Fixture.Foreign.Id<T>");
    }

    private sealed record ForeignPersistenceFixture(string Name, string Declaration, bool IsAllowed, string TargetType);

    private sealed record UserQueryFixture(string Name, string Query, string? ExpectedViolation);
}
