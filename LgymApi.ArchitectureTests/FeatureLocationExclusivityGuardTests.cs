using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using LgymApi.Api.Interfaces;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class FeatureLocationExclusivityGuardTests
{
    [Test]
    public void ContractDto_Types_Only_Appear_Under_Contracts()
    {
        var (repoRoot, compilation, syntaxTrees) = PrepareCompilation();
        var idtoSymbol = compilation.GetTypeByMetadataName(typeof(IDto).FullName!);
        var resultDtoSymbol = compilation.GetTypeByMetadataName(typeof(IResultDto).FullName!);

        Assert.Multiple(() =>
        {
            Assert.That(idtoSymbol, Is.Not.Null, "Unable to resolve IDto symbol.");
            Assert.That(resultDtoSymbol, Is.Not.Null, "Unable to resolve IResultDto symbol.");
        });

        var violations = new List<Violation>();

        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();

            foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
                if (typeSymbol == null)
                {
                    continue;
                }

                if (!IsProjectType(typeSymbol, compilation.Assembly))
                {
                    continue;
                }

                if (!ImplementsDtoInterface(typeSymbol, idtoSymbol!, resultDtoSymbol!))
                {
                    continue;
                }

                var filePath = tree.FilePath;
                if (IsContractsPath(filePath))
                {
                    continue;
                }

                var line = typeDeclaration.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var relativePath = Path.GetRelativePath(repoRoot, filePath);
                violations.Add(new Violation(relativePath, line, typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Types implementing IDto or IResultDto must be contained in Features/*/Contracts folders only." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    [Test]
    public void ControllerBase_Derived_Types_Only_Live_Under_Controllers()
    {
        var (repoRoot, compilation, syntaxTrees) = PrepareCompilation();
        var controllerBaseSymbol = compilation.GetTypeByMetadataName(typeof(ControllerBase).FullName!);

        Assert.That(controllerBaseSymbol, Is.Not.Null, "Unable to resolve ControllerBase symbol.");

        var violations = new List<Violation>();

        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();

            foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                if (typeSymbol == null)
                {
                    continue;
                }

                if (!IsProjectType(typeSymbol, compilation.Assembly))
                {
                    continue;
                }

                if (!InheritsFromControllerBase(typeSymbol, controllerBaseSymbol!))
                {
                    continue;
                }

                var filePath = tree.FilePath;
                if (IsControllersPath(filePath))
                {
                    continue;
                }

                var line = classDeclaration.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var relativePath = Path.GetRelativePath(repoRoot, filePath);
                violations.Add(new Violation(relativePath, line, typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "Controllers inheriting ControllerBase must be declared only inside Features/*/Controllers folders." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    [Test]
    public void FluentValidation_Validators_Only_Live_Under_Validation()
    {
        var (repoRoot, compilation, syntaxTrees) = PrepareCompilation();
        var abstractValidatorSymbol = compilation.GetTypeByMetadataName(typeof(AbstractValidator<>).FullName!);

        Assert.That(abstractValidatorSymbol, Is.Not.Null, "Unable to resolve AbstractValidator<T> symbol.");

        var violations = new List<Violation>();

        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();

            foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(classDeclaration) as INamedTypeSymbol;
                if (typeSymbol == null)
                {
                    continue;
                }

                if (!IsProjectType(typeSymbol, compilation.Assembly))
                {
                    continue;
                }

                if (!InheritsFromAbstractValidator(typeSymbol, abstractValidatorSymbol!))
                {
                    continue;
                }

                var filePath = tree.FilePath;
                if (IsValidationPath(filePath))
                {
                    continue;
                }

                var line = classDeclaration.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var relativePath = Path.GetRelativePath(repoRoot, filePath);
                violations.Add(new Violation(relativePath, line, typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }

        Assert.That(
            violations,
            Is.Empty,
            "FluentValidation validators inheriting AbstractValidator<T> must live only within Features/*/Validation folders." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    private static (string RepoRoot, CSharpCompilation Compilation, IReadOnlyList<SyntaxTree> SyntaxTrees) PrepareCompilation()
    {
        var result = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Api");

        Assert.That(result.SyntaxTrees, Is.Not.Empty, "No production API source files found for the feature location guard test.");

        return result;
    }

    private static bool ImplementsDtoInterface(INamedTypeSymbol typeSymbol, INamedTypeSymbol idtoSymbol, INamedTypeSymbol resultDtoSymbol)
    {
        if (typeSymbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, idtoSymbol)))
        {
            return true;
        }

        return typeSymbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, resultDtoSymbol));
    }

    private static bool InheritsFromControllerBase(INamedTypeSymbol typeSymbol, INamedTypeSymbol controllerBaseSymbol)
    {
        var current = typeSymbol;
        while (current.BaseType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current.BaseType, controllerBaseSymbol))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool InheritsFromAbstractValidator(INamedTypeSymbol typeSymbol, INamedTypeSymbol abstractValidatorSymbol)
    {
        var current = typeSymbol.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, abstractValidatorSymbol))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool IsProjectType(INamedTypeSymbol typeSymbol, IAssemblySymbol projectAssembly)
    {
        return SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingAssembly, projectAssembly);
    }

    private static bool IsContractsPath(string path)
    {
        return ArchitectureTestHelpers.IsApiFeatureLeafFilePath(path, "Contracts");
    }

    private static bool IsControllersPath(string path)
    {
        return ArchitectureTestHelpers.IsApiFeatureLeafFilePath(path, "Controllers");
    }

    private static bool IsValidationPath(string path)
    {
        return ArchitectureTestHelpers.IsApiFeatureLeafFilePath(path, "Validation");
    }

    private sealed record Violation(string File, int Line, string TypeName)
    {
        public override string ToString() => $"{File}:{Line} {TypeName}";
    }
}
