using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class IdentityAccountContractBoundaryGuardTests
{
    private const string ContractsPath = "LgymApi.Identity/Contracts/Accounts/";
    private const string UserMetadataName = "LgymApi.Domain.Entities.User";
    private const string RoleMetadataName = "LgymApi.Domain.Entities.Role";
    private const string TypedIdMetadataName = "LgymApi.Domain.ValueObjects.Id`1";

    [Test]
    public void IdentityAccountContracts_Should_NotExposeUserEntitiesOrRepositories()
    {
        var (_, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Identity");

        Assert.That(
            CollectViolations(compilation, syntaxTrees.Where(tree => IsContractPath(tree.FilePath))),
            Is.Empty,
            "Identity account contracts must expose account facts and typed identifiers only.");
    }

    [TestCase(
        "UserEntityLeak.cs",
        """
        using LgymApi.Domain.Entities;

        namespace LgymApi.Application.Identity.Contracts.Accounts;

        public sealed record UserEntityLeak(User User);
        """,
        UserMetadataName)]
    [TestCase(
        "NestedEntityLeak.cs",
        """
        using LgymApi.Application.BuildingBlocks.Results;
        using LgymApi.Domain.Entities;

        namespace LgymApi.Identity.Contracts.Accounts;

        public sealed record NestedEntityLeak(Result<IReadOnlyList<Role>, string> Roles);
        """,
        RoleMetadataName)]
    public void IdentityAccountContractFixture_WithImplementationLeak_IsRejected(
        string fileName,
        string source,
        string expectedLeak)
    {
        var (repositoryRoot, compilation, _) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Identity");
        var fixture = CSharpSyntaxTree.ParseText(
            source,
            path: Path.Combine(repositoryRoot, ContractsPath, fileName));

        var violations = CollectViolations(compilation.AddSyntaxTrees(fixture), [fixture]);

        Assert.That(violations.Select(violation => violation.LeakedType), Does.Contain(expectedLeak));
    }

    private static IReadOnlyList<Violation> CollectViolations(
        CSharpCompilation compilation,
        IEnumerable<SyntaxTree> syntaxTrees)
    {
        var violations = new Dictionary<string, Violation>(StringComparer.Ordinal);
        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            foreach (var declaration in tree.GetRoot()
                         .DescendantNodes()
                         .OfType<MemberDeclarationSyntax>()
                         .Where(declaration => declaration is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type || !IsPubliclyVisible(type))
                {
                    continue;
                }

                foreach (var typeSyntax in declaration.DescendantNodes().OfType<TypeSyntax>().Where(IsOuterTypeSyntax))
                {
                    foreach (var exposedType in EnumerateNamedTypes(semanticModel.GetTypeInfo(typeSyntax).Type))
                    {
                        var metadataName = GetMetadataName(exposedType.OriginalDefinition);
                        if (metadataName is not (UserMetadataName or RoleMetadataName))
                        {
                            continue;
                        }

                        var violation = new Violation(
                            ArchitectureTestHelpers.NormalizePath(tree.FilePath),
                            type.Name,
                            typeSyntax.ToString(),
                            metadataName);
                        violations.TryAdd(violation.Identity, violation);
                    }
                }
            }
        }

        return violations.Values.OrderBy(violation => violation.Identity, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNamedTypes(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            yield break;
        }

        yield return namedType;
        if (GetMetadataName(namedType.OriginalDefinition) == TypedIdMetadataName)
        {
            yield break;
        }

        foreach (var typeArgument in namedType.TypeArguments)
        {
            foreach (var nestedType in EnumerateNamedTypes(typeArgument))
            {
                yield return nestedType;
            }
        }
    }

    private static bool IsContractPath(string path)
    {
        var normalizedPath = ArchitectureTestHelpers.NormalizePath(path);
        return normalizedPath.StartsWith(ContractsPath, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains($"/{ContractsPath}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOuterTypeSyntax(TypeSyntax typeSyntax)
        => !typeSyntax.Ancestors().OfType<TypeSyntax>().Any();

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

    private sealed record Violation(string Path, string SourceType, string SourceSyntax, string LeakedType)
    {
        public string Identity => $"{Path}|{SourceType}|{SourceSyntax}|{LeakedType}";
    }
}
