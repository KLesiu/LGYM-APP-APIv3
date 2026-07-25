using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModulePublicSurfaceGuardTests
{
    private const string GuardId = nameof(ModulePublicSurfaceGuardTests);

    [Test]
    public void Module_Public_Surface_Should_Be_Limited_To_Contracts_Registration_Helpers_And_Explicit_Events()
    {
        var (repoRoot, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application", "LgymApi.Infrastructure");

        var observedViolations = syntaxTrees
            .SelectMany(tree => CollectViolationsForTree(repoRoot, compilation, tree))
            .OrderBy(violation => violation.IdentityKey, StringComparer.Ordinal)
            .ToList();

        ArchitectureTestHelpers.AssertNoUnexpectedModuleBoundaryViolations(GuardId, observedViolations);
    }

    [Test]
    public void Public_Delegate_In_BuildingBlocks_Result_Fixture_Is_Rejected()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var path = Path.Combine(repoRoot, "LgymApi.Application", "BuildingBlocks", "Results", "Result.cs");
        var tree = CSharpSyntaxTree.ParseText(
            "namespace LgymApi.Application.BuildingBlocks.Results; public delegate void ResultCallback();",
            path: path);
        var compilation = ArchitectureTestHelpers.CreateCompilation([tree]);

        var violations = CollectViolationsForTree(repoRoot, compilation, tree).ToList();

        Assert.That(violations, Is.Not.Empty);
    }

    private static IEnumerable<ModuleBoundaryObservedViolation> CollectViolationsForTree(
        string repoRoot,
        Compilation compilation,
        SyntaxTree tree)
    {
        var moduleName = ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(tree.FilePath);
        if (moduleName == null)
        {
            yield break;
        }

        var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
        var root = tree.GetCompilationUnitRoot();
        var relativePath = ArchitectureTestHelpers.NormalizePath(Path.GetRelativePath(repoRoot, tree.FilePath));

        var declarations = root.Members.OfType<BaseTypeDeclarationSyntax>()
            .Cast<MemberDeclarationSyntax>()
            .Concat(root.DescendantNodes().OfType<DelegateDeclarationSyntax>());
        foreach (var declaration in declarations)
        {
            if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol declaredSymbol)
            {
                continue;
            }

            if (declaredSymbol.DeclaredAccessibility != Accessibility.Public
                || (declaration is not DelegateDeclarationSyntax
                    && IsAllowedPublicSurface(relativePath, (BaseTypeDeclarationSyntax)declaration, declaredSymbol)))
            {
                continue;
            }

            yield return new ModuleBoundaryObservedViolation(
                GuardId,
                moduleName,
                moduleName,
                relativePath,
                declaredSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        }
    }

    private static bool IsAllowedPublicSurface(string relativePath, BaseTypeDeclarationSyntax declaration, INamedTypeSymbol declaredSymbol)
    {
        if (declaration is InterfaceDeclarationSyntax)
        {
            return IsAllowedPublicInterface(relativePath, declaredSymbol);
        }

        if (declaration is EnumDeclarationSyntax)
        {
            return IsAllowedPublicEnum(relativePath);
        }

        if (relativePath.EndsWith("ServiceCollectionExtensions.cs", StringComparison.Ordinal))
        {
            return true;
        }

        if (relativePath.Contains("/Models/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Contracts/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Events/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return declaredSymbol.Name.EndsWith("Event", StringComparison.Ordinal);
    }

    private static bool IsAllowedPublicInterface(string relativePath, INamedTypeSymbol declaredSymbol)
    {
        if (!declaredSymbol.Name.StartsWith("I", StringComparison.Ordinal))
        {
            return false;
        }

        if (relativePath.Contains("/Contracts/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Events/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Repositories/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Abstractions/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Mapping/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Pagination/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Units/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return declaredSymbol.Name.EndsWith("Service", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("ServiceDependencies", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Builder", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Bridge", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Publisher", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Provider", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Tracker", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Store", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Calculator", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Policy", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Registry", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Profile", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Context", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Strategy", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Converter", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Validator", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Dispatcher", StringComparison.Ordinal)
            || declaredSymbol.Name.EndsWith("Settings", StringComparison.Ordinal)
            || declaredSymbol.Name.Equals("IUnitOfWork", StringComparison.Ordinal)
            || declaredSymbol.Name.Equals("IUnitOfWorkTransaction", StringComparison.Ordinal);
    }

    private static bool IsAllowedPublicEnum(string relativePath)
    {
        return relativePath.Contains("/Models/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Contracts/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Events/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Pagination/", StringComparison.OrdinalIgnoreCase)
            || relativePath.Contains("/Options/", StringComparison.OrdinalIgnoreCase);
    }
}
