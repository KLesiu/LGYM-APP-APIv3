using LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts;
using LgymApi.Domain.ValueObjects;
using LgymApi.Platform.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class AppConfigAuthorizationBoundaryGuardTests
{
    [Test]
    public void AppConfigAuthorizationPort_ShouldHaveExactConsumerOwnedSignature()
    {
        var contract = typeof(IAppConfigAuthorizationPort);
        var method = contract.GetMethods().Single();
        var parameters = method.GetParameters();

        Assert.Multiple(() =>
        {
            Assert.That(contract.Namespace, Is.EqualTo("LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts"));
            Assert.That(method.Name, Is.EqualTo("CanManageAppConfigAsync"));
            Assert.That(method.ReturnType, Is.EqualTo(typeof(Task<bool>)));
            Assert.That(parameters.Select(parameter => parameter.ParameterType), Is.EqualTo(new[]
            {
                typeof(Id<ActorReference>),
                typeof(CancellationToken)
            }));
            Assert.That(parameters[1].IsOptional, Is.True);
        });
    }

    [Test]
    public void AppConfigAuthorizationPort_ShouldUseDefaultCancellationTokenSyntax()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var filePath = Path.Combine(
            repositoryRoot,
            "LgymApi.Platform",
            "ReferenceData",
            "AppConfig",
            "Contracts",
            "IAppConfigAuthorizationPort.cs");
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(filePath), path: filePath);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var cancellationToken = method.ParameterList.Parameters[1];

        Assert.Multiple(() =>
        {
            Assert.That(method.ParameterList.Parameters, Has.Count.EqualTo(2));
            Assert.That(cancellationToken.Identifier.ValueText, Is.EqualTo("cancellationToken"));
            Assert.That(cancellationToken.Default?.Value.IsKind(SyntaxKind.DefaultLiteralExpression), Is.True);
        });
    }

    [TestCase("Fixture.ReferenceData.AppConfig.Contracts.IAppConfigAuthorizationPort", true)]
    [TestCase("Fixture.Identity.Contracts.Access.IUserAdminAccessService", false)]
    [TestCase("Fixture.Identity.Repositories.IUserRepository", false)]
    [TestCase("Fixture.Identity.Repositories.IRoleRepository", false)]
    [TestCase("Fixture.Identity.Authorization.IGenericPermissionService", false)]
    [TestCase("Fixture.Domain.Entities.User", false)]
    [TestCase("Fixture.Domain.ValueObjects.Id<Fixture.Domain.Entities.Role>", false)]
    public void AppConfigDependencyFixture_ShouldAllowOnlyConsumerOwnedAuthorizationPort(
        string dependencyType,
        bool isAllowed)
    {
        var violations = CollectAppConfigDependencyViolations(dependencyType);

        Assert.That(violations, isAllowed ? Is.Empty : Has.Count.EqualTo(1));
        if (!isAllowed)
        {
            Assert.That(violations.Single(), Is.EqualTo(dependencyType));
        }
    }

    [TestCase(
        "Task<bool> CanManageAppConfigAsync(Id<ActorReference> actorId, CancellationToken cancellationToken = default);",
        true)]
    [TestCase(
        "Task<bool> CanManageAppConfigAsync(Id<User> userId, CancellationToken cancellationToken = default);",
        false)]
    [TestCase(
        "Task<bool> CanManageAppConfigAsync(Id<Role> roleId, CancellationToken cancellationToken = default);",
        false)]
    [TestCase(
        "Task<bool> CanManageAppConfigAsync(Id<User> userId, string permission, CancellationToken cancellationToken = default);",
        false)]
    public void AppConfigAuthorizationPortShapeFixture_ShouldAllowOnlyActorReferenceAndNoPermissionParameter(
        string declaration,
        bool isAllowed)
    {
        var violations = CollectPortShapeViolations(declaration);

        Assert.That(violations, isAllowed ? Is.Empty : Is.Not.Empty);
    }

    private static IReadOnlyList<string> CollectAppConfigDependencyViolations(string dependencyType)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using System.Threading;
            using System.Threading.Tasks;
            using Fixture.Domain.Entities;
            using Fixture.Domain.ValueObjects;
            using Fixture.Platform.Contracts;
            using Fixture.Platform.Contracts;

            namespace Fixture.Domain.Entities { public sealed class User { } public sealed class Role { } }
            namespace Fixture.Platform.Contracts { public sealed class ActorReference { private ActorReference() { } } }
            namespace Fixture.Domain.ValueObjects { public readonly record struct Id<T>; }
            namespace Fixture.ReferenceData.AppConfig.Contracts { public interface IAppConfigAuthorizationPort { Task<bool> CanManageAppConfigAsync(Fixture.Domain.ValueObjects.Id<Fixture.Platform.Contracts.ActorReference> actorId, CancellationToken cancellationToken = default); } }
            namespace Fixture.Identity.Contracts.Access { public interface IUserAdminAccessService { } }
            namespace Fixture.Identity.Repositories { public interface IUserRepository { } public interface IRoleRepository { } }
            namespace Fixture.Identity.Authorization { public interface IGenericPermissionService { Task<bool> HasPermissionAsync(Fixture.Domain.ValueObjects.Id<Fixture.Domain.Entities.User> userId, string permission, CancellationToken cancellationToken = default); } }
            namespace Fixture.ReferenceData.AppConfig { public sealed class AppConfigService { public {{dependencyType}} Dependency { get; init; } = default!; } }
            """, path: "LgymApi.Application/Platform/ReferenceData/AppConfig/AppConfigDependencyFixture.cs");
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);
        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "The compiler fixture must be valid before the AppConfig dependency rule is evaluated.");

        var property = tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText == "Dependency");
        var targetType = compilation.GetSemanticModel(tree).GetTypeInfo(property.Type).Type;
        var allowedPort = compilation.GetTypeByMetadataName("Fixture.ReferenceData.AppConfig.Contracts.IAppConfigAuthorizationPort");

        Assert.That(targetType, Is.Not.Null, "The compiler fixture must bind the dependency type.");
        Assert.That(allowedPort, Is.Not.Null, "The compiler fixture must bind the AppConfig authorization port.");

        return SymbolEqualityComparer.Default.Equals(targetType, allowedPort)
            ? []
            : [targetType!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)];
    }

    private static IReadOnlyList<string> CollectPortShapeViolations(string declaration)
    {
        var tree = CSharpSyntaxTree.ParseText($$"""
            using System.Threading;
            using System.Threading.Tasks;
            using Fixture.Domain.Entities;
            using Fixture.Domain.ValueObjects;
            using Fixture.Platform.Contracts;

            namespace Fixture.Domain.Entities { public sealed class User { } public sealed class Role { } }
            namespace Fixture.Platform.Contracts { public sealed class ActorReference { private ActorReference() { } } }
            namespace Fixture.Domain.ValueObjects { public readonly record struct Id<T>; }
            namespace Fixture.ReferenceData.AppConfig.Contracts { public interface IAppConfigAuthorizationPort { {{declaration}} } }
            """, path: "LgymApi.Application/Platform/ReferenceData/AppConfig/Contracts/IAppConfigAuthorizationPort.cs");
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);
        Assert.That(
            compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty,
            "The compiler fixture must be valid before the AppConfig authorization-port shape is evaluated.");

        var contract = compilation.GetTypeByMetadataName("Fixture.ReferenceData.AppConfig.Contracts.IAppConfigAuthorizationPort");
        var actorReference = compilation.GetTypeByMetadataName("Fixture.Platform.Contracts.ActorReference");
        Assert.That(contract, Is.Not.Null);
        Assert.That(actorReference, Is.Not.Null);

        var method = contract!.GetMembers().OfType<IMethodSymbol>().Single();
        var violations = new List<string>();
        if (method.Name != "CanManageAppConfigAsync")
        {
            violations.Add("Method name must be CanManageAppConfigAsync.");
        }

        if (!IsTaskOfBoolean(method.ReturnType))
        {
            violations.Add("Return type must be Task<bool>.");
        }

        if (method.Parameters.Length != 2)
        {
            violations.Add("The port must have exactly userId and cancellationToken parameters.");
            return violations;
        }

        if (!IsIdOfActorReference(method.Parameters[0].Type, actorReference!))
        {
            violations.Add("The first parameter must be Id<ActorReference>.");
        }

        if (method.Parameters[1].Type.ToDisplayString() != typeof(CancellationToken).FullName
            || !method.Parameters[1].IsOptional)
        {
            violations.Add("The second parameter must be optional CancellationToken.");
        }

        return violations;
    }

    private static bool IsTaskOfBoolean(ITypeSymbol type)
    {
        return type is INamedTypeSymbol { Name: "Task", Arity: 1 } task
               && task.TypeArguments[0].SpecialType == SpecialType.System_Boolean;
    }

    private static bool IsIdOfActorReference(ITypeSymbol type, INamedTypeSymbol actorReference)
    {
        return type is INamedTypeSymbol { Name: "Id", Arity: 1 } id
               && SymbolEqualityComparer.Default.Equals(id.TypeArguments[0], actorReference);
    }
}
