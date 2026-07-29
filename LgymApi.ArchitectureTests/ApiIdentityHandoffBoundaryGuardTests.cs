using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ApiIdentityHandoffBoundaryGuardTests
{
    private const string UserMetadataName = "LgymApi.Domain.Entities.User";
    private const string RoleMetadataName = "LgymApi.Domain.Entities.Role";

    private static readonly HashSet<string> IdentityRepositoryMetadataNames = new(StringComparer.Ordinal)
    {
        "LgymApi.Application.Repositories.IRoleRepository",
        "LgymApi.Application.Repositories.IUserExternalLoginRepository",
        "LgymApi.Application.Repositories.IUserRepository"
    };

    private static readonly HashSet<string> LegacyHelperNames = new(StringComparer.Ordinal)
    {
        "GetCurrentUser",
        "GetCurrentUserId",
        "ParseRouteUserIdForCurrentAdmin",
        "ParseRouteUserIdForCurrentUser"
    };

    [Test]
    public void ApiMiddlewareAndControllers_Should_Have_NoIdentityEntityRepositoryOrLegacyHelperLeaks()
    {
        var (_, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Api");

        var violations = CollectViolations(compilation, syntaxTrees);

        Assert.That(
            violations,
            Is.Empty,
            "API middleware and controllers must use marker-safe account context and compatibility ports only." +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [TestCase(
        "Controllers/UserEntityLeakController.cs",
        """
        namespace LgymApi.Api.Features.Test.Controllers;
        public sealed class UserEntityLeakController { private global::LgymApi.Domain.Entities.User? _user; }
        """,
        "entity")]
    [TestCase(
        "Middleware/RoleIdLeakMiddleware.cs",
        """
        using LgymApi.Domain.Entities;
        using LgymApi.Domain.ValueObjects;
        namespace LgymApi.Api.Middleware;
        public sealed class RoleIdLeakMiddleware { private Id<Role> _roleId; }
        """,
        "entity")]
    [TestCase(
        "Controllers/UserRepositoryLeakController.cs",
        """
        using LgymApi.Application.Repositories;
        namespace LgymApi.Api.Features.Test.Controllers;
        public sealed class UserRepositoryLeakController(IUserRepository repository) { }
        """,
        "repository")]
    [TestCase(
        "Middleware/UserItemLeakMiddleware.cs",
        """
        using Microsoft.AspNetCore.Http;
        namespace LgymApi.Api.Middleware;
        public sealed class UserItemLeakMiddleware
        {
            public void Invoke(HttpContext context) { _ = context.Items["User"]; }
        }
        """,
        "user-item")]
    [TestCase(
        "Controllers/LegacyHelperLeakController.cs",
        """
        namespace LgymApi.Api.Features.Test.Controllers;
        public sealed class LegacyHelperLeakController
        {
            public void Invoke() { _ = GetCurrentUserId(); }
            private static object GetCurrentUserId() => new();
        }
        """,
        "legacy-helper")]
    [TestCase(
        "Controllers/LegacyRouteHelperLeakController.cs",
        """
        namespace LgymApi.Api.Features.Test.Controllers;
        public sealed class LegacyRouteHelperLeakController
        {
            public void Invoke() { _ = ParseRouteUserIdForCurrentUser(); }
            private static object ParseRouteUserIdForCurrentUser() => new();
        }
        """,
        "legacy-helper")]
    public void ForbiddenSemanticFixtures_AreRejected(string relativePath, string source, string expectedKind)
    {
        var (repositoryRoot, compilation, _) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Api");
        var fixture = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
            Path.Combine(repositoryRoot, "LgymApi.Api", relativePath));

        var violations = CollectViolations(compilation.AddSyntaxTrees(fixture), [fixture]);

        Assert.That(violations.Select(violation => violation.Kind), Does.Contain(expectedKind));
    }

    [Test]
    public void MarkerSafeFixture_IsAccepted_AndValidationPathsRemainOutsideThisGuard()
    {
        var (repositoryRoot, compilation, _) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Api");
        var markerFixture = CSharpSyntaxTree.ParseText(
            """
            using LgymApi.Domain.ValueObjects;
            using LgymApi.Identity.Contracts;
            namespace LgymApi.Api.Features.Test.Controllers;
            public sealed class MarkerSafeController { private Id<AccountReference> _accountId; }
            """,
            path: Path.Combine(repositoryRoot, "LgymApi.Api/Features/Test/Controllers/MarkerSafeController.cs"));
        var validatorFixture = CSharpSyntaxTree.ParseText(
            """
            using LgymApi.Domain.Entities;
            namespace LgymApi.Api.Features.Test.Validation;
            public sealed class ExistingValidatorScope { private User? _user; }
            """,
            path: Path.Combine(repositoryRoot, "LgymApi.Api/Features/Test/Validation/ExistingValidatorScope.cs"));
        var fixtureCompilation = compilation.AddSyntaxTrees(markerFixture, validatorFixture);

        var violations = CollectViolations(fixtureCompilation, [markerFixture, validatorFixture]);

        Assert.That(violations, Is.Empty);
    }

    [Test]
    public void ApiComposition_Should_InvokeTask7CompatibilityExactlyOnce()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var programPath = Path.Combine(repositoryRoot, "LgymApi.Api", "Program.cs");
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(programPath)).GetCompilationUnitRoot();
        var callCount = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(invocation => GetInvokedMethodName(invocation) == "AddTask7ApiCompatibility");

        Assert.That(callCount, Is.EqualTo(1));
    }

    private static IReadOnlyList<Violation> CollectViolations(
        CSharpCompilation compilation,
        IEnumerable<SyntaxTree> syntaxTrees)
    {
        var violations = new Dictionary<string, Violation>(StringComparer.Ordinal);
        foreach (var tree in syntaxTrees.Where(tree => IsGuardedPath(tree.FilePath)))
        {
            var root = tree.GetRoot();
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);

            foreach (var typeSyntax in root.DescendantNodesAndSelf().OfType<TypeSyntax>())
            {
                AddTypeViolations(semanticModel.GetTypeInfo(typeSyntax).Type, tree, typeSyntax, violations);
            }

            foreach (var simpleName in root.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
            {
                AddSymbolViolations(semanticModel.GetSymbolInfo(simpleName).Symbol, tree, simpleName, violations);
            }

            foreach (var invocation in root.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                var methodName = (semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol)?.Name
                    ?? GetInvokedMethodName(invocation);
                if (methodName != null && LegacyHelperNames.Contains(methodName))
                {
                    AddViolation(tree, invocation, "legacy-helper", methodName, violations);
                }
            }

            foreach (var elementAccess in root.DescendantNodesAndSelf().OfType<ElementAccessExpressionSyntax>())
            {
                if (elementAccess.ArgumentList.Arguments.Any(argument =>
                        semanticModel.GetConstantValue(argument.Expression) is { HasValue: true, Value: "User" }))
                {
                    AddViolation(tree, elementAccess, "user-item", "Items[\"User\"]", violations);
                }
            }
        }

        return violations.Values.OrderBy(violation => violation.Identity, StringComparer.Ordinal).ToList();
    }

    private static void AddSymbolViolations(
        ISymbol? symbol,
        SyntaxTree tree,
        SyntaxNode node,
        IDictionary<string, Violation> violations)
    {
        switch (symbol)
        {
            case IAliasSymbol alias:
                AddTypeViolations(alias.Target as ITypeSymbol, tree, node, violations);
                break;
            case IFieldSymbol field:
                AddTypeViolations(field.Type, tree, node, violations);
                break;
            case ILocalSymbol local:
                AddTypeViolations(local.Type, tree, node, violations);
                break;
            case IParameterSymbol parameter:
                AddTypeViolations(parameter.Type, tree, node, violations);
                break;
            case IPropertySymbol property:
                AddTypeViolations(property.Type, tree, node, violations);
                break;
            case IMethodSymbol method:
                AddTypeViolations(method.ReturnType, tree, node, violations);
                foreach (var typeArgument in method.TypeArguments)
                {
                    AddTypeViolations(typeArgument, tree, node, violations);
                }
                break;
            case INamedTypeSymbol namedType:
                AddTypeViolations(namedType, tree, node, violations);
                break;
        }
    }

    private static void AddTypeViolations(
        ITypeSymbol? type,
        SyntaxTree tree,
        SyntaxNode node,
        IDictionary<string, Violation> violations)
    {
        foreach (var namedType in EnumerateNamedTypes(type))
        {
            var metadataName = GetMetadataName(namedType.OriginalDefinition);
            if (metadataName is UserMetadataName or RoleMetadataName)
            {
                AddViolation(tree, node, "entity", metadataName, violations);
            }
            else if (IdentityRepositoryMetadataNames.Contains(metadataName))
            {
                AddViolation(tree, node, "repository", metadataName, violations);
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(ITypeSymbol? type)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            return EnumerateNamedTypes(arrayType.ElementType);
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return [];
        }

        return new[] { namedType }.Concat(namedType.TypeArguments.SelectMany(EnumerateNamedTypes));
    }

    private static void AddViolation(
        SyntaxTree tree,
        SyntaxNode node,
        string kind,
        string detail,
        IDictionary<string, Violation> violations)
    {
        var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
        var violation = new Violation(ArchitectureTestHelpers.NormalizePath(tree.FilePath), line, kind, detail);
        violations.TryAdd(violation.Identity, violation);
    }

    private static bool IsGuardedPath(string path)
    {
        var normalizedPath = ArchitectureTestHelpers.NormalizePath(path);
        return normalizedPath.Contains("/LgymApi.Api/Middleware/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/Controllers/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetInvokedMethodName(InvocationExpressionSyntax invocation)
        => invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            _ => null
        };

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

    private sealed record Violation(string Path, int Line, string Kind, string Detail)
    {
        public string Identity => $"{Path}|{Line}|{Kind}|{Detail}";
        public override string ToString() => $"{Path}:{Line} {Kind}: {Detail}";
    }
}
