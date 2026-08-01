using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NoHostDependencyGuardTests
{
    [Test]
    public void Application_And_Domain_Modules_Should_Not_Depend_On_Api_Host_Concerns()
    {
        var (repoRoot, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation("LgymApi.Application", "LgymApi.Domain");
        var violations = new Dictionary<string, HostDependencyViolation>(StringComparer.Ordinal);

        foreach (var tree in syntaxTrees)
        {
            var sourceFile = ArchitectureTestHelpers.ClassifyModuleBoundaryFile(tree.FilePath, repoRoot);
            if (sourceFile.IsExcluded)
            {
                continue;
            }

            var sourceModule = TryGetSourceModule(sourceFile.RelativePath);
            if (string.IsNullOrWhiteSpace(sourceModule))
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var root = tree.GetCompilationUnitRoot();

            foreach (var usingDirective in root.Usings)
            {
                var usingTarget = usingDirective.Name?.ToString();
                if (!IsForbiddenUsing(usingTarget, out var forbiddenTarget))
                {
                    continue;
                }

                var sourceSymbol = GetEnclosingSourceSymbol(semanticModel, usingDirective) ?? sourceFile.RelativePath;
                var violation = new HostDependencyViolation(sourceModule, sourceSymbol, forbiddenTarget, sourceFile.RelativePath, usingDirective.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                violations.TryAdd(violation.IdentityKey, violation);
            }

            foreach (var typeSyntax in root.DescendantNodes().OfType<TypeSyntax>())
            {
                var symbol = semanticModel.GetTypeInfo(typeSyntax).Type;
                if (!TryGetForbiddenTarget(symbol, typeSyntax.ToString(), out var forbiddenTarget))
                {
                    continue;
                }

                var sourceSymbol = GetEnclosingSourceSymbol(semanticModel, typeSyntax) ?? sourceFile.RelativePath;
                var line = typeSyntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var violation = new HostDependencyViolation(sourceModule, sourceSymbol, forbiddenTarget, sourceFile.RelativePath, line);
                violations.TryAdd(violation.IdentityKey, violation);
            }
        }

        Assert.That(
            violations.Values.OrderBy(v => v.IdentityKey, StringComparer.Ordinal).ToList(),
            Is.Empty,
            "Application/Domain module code must not depend on API host concerns (LgymApi.Api, ControllerBase, HttpContext, Hangfire)." + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Values.OrderBy(v => v.IdentityKey, StringComparer.Ordinal).Select(v => v.ToString())));
    }

    private static bool IsForbiddenUsing(string? usingTarget, out string forbiddenTarget)
    {
        if (!string.IsNullOrWhiteSpace(usingTarget) && usingTarget.StartsWith("LgymApi.Api", StringComparison.Ordinal))
        {
            forbiddenTarget = "LgymApi.Api";
            return true;
        }

        forbiddenTarget = string.Empty;
        return false;
    }

    private static bool TryGetForbiddenTarget(ITypeSymbol? symbol, string syntaxText, out string forbiddenTarget)
    {
        if (symbol != null)
        {
            var containingNamespace = symbol.ContainingNamespace?.ToDisplayString();
            var metadataName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);

            if (metadataName.StartsWith("LgymApi.Api.", StringComparison.Ordinal))
            {
                forbiddenTarget = "LgymApi.Api";
                return true;
            }

            if (string.Equals(symbol.Name, "ControllerBase", StringComparison.Ordinal)
                && string.Equals(containingNamespace, "Microsoft.AspNetCore.Mvc", StringComparison.Ordinal))
            {
                forbiddenTarget = "Microsoft.AspNetCore.Mvc.ControllerBase";
                return true;
            }

            if (string.Equals(symbol.Name, "HttpContext", StringComparison.Ordinal)
                && string.Equals(containingNamespace, "Microsoft.AspNetCore.Http", StringComparison.Ordinal))
            {
                forbiddenTarget = "Microsoft.AspNetCore.Http.HttpContext";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(containingNamespace)
                && (string.Equals(containingNamespace, "Hangfire", StringComparison.Ordinal)
                    || containingNamespace.StartsWith("Hangfire.", StringComparison.Ordinal)))
            {
                forbiddenTarget = containingNamespace.StartsWith("Hangfire.", StringComparison.Ordinal)
                    ? containingNamespace
                    : $"Hangfire.{symbol.Name}";
                return true;
            }
        }

        if (syntaxText.Contains("LgymApi.Api", StringComparison.Ordinal))
        {
            forbiddenTarget = "LgymApi.Api";
            return true;
        }

        forbiddenTarget = string.Empty;
        return false;
    }

    private static string? GetEnclosingSourceSymbol(SemanticModel semanticModel, SyntaxNode node)
    {
        foreach (var current in node.AncestorsAndSelf())
        {
            ISymbol? symbol = current switch
            {
                MethodDeclarationSyntax method => semanticModel.GetDeclaredSymbol(method),
                ConstructorDeclarationSyntax constructor => semanticModel.GetDeclaredSymbol(constructor),
                PropertyDeclarationSyntax property => semanticModel.GetDeclaredSymbol(property),
                FieldDeclarationSyntax field when field.Declaration.Variables.FirstOrDefault() is { } variable => semanticModel.GetDeclaredSymbol(variable),
                EventDeclarationSyntax @event => semanticModel.GetDeclaredSymbol(@event),
                ClassDeclarationSyntax @class => semanticModel.GetDeclaredSymbol(@class),
                InterfaceDeclarationSyntax @interface => semanticModel.GetDeclaredSymbol(@interface),
                RecordDeclarationSyntax record => semanticModel.GetDeclaredSymbol(record),
                StructDeclarationSyntax @struct => semanticModel.GetDeclaredSymbol(@struct),
                _ => null
            };

            if (symbol != null)
            {
                return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal);
            }
        }

        return null;
    }

    private static string? TryGetSourceModule(string relativePath)
    {
        var normalized = ArchitectureTestHelpers.NormalizePath(relativePath);

        if (normalized.StartsWith("LgymApi.Domain/", StringComparison.OrdinalIgnoreCase))
        {
            return "Domain";
        }

        return normalized switch
        {
            var path when path.StartsWith("LgymApi.Application/User/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Role/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/EloRegistry/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/ExternalAuth/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Features/Tutorial/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Features/PasswordReset/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Features/AdminManagement/", StringComparison.OrdinalIgnoreCase)
                => "Identity & Accounts",
            var path when path.StartsWith("LgymApi.Notifications/", StringComparison.OrdinalIgnoreCase)
                => "Notifications",
            var path when path.StartsWith("LgymApi.Application/Features/Reporting/", StringComparison.OrdinalIgnoreCase)
                => "Reporting",
            var path when path.StartsWith("LgymApi.Application/TrainingPlanning/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/PlanDay/", StringComparison.OrdinalIgnoreCase)
                => "Training Planning",
            var path when path.StartsWith("LgymApi.Application/Training/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Exercise/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/ExerciseScores/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Gym/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/MainRecords/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Measurements/", StringComparison.OrdinalIgnoreCase)
                => "Workout & Progress",
            var path when path.StartsWith("LgymApi.Application/TrainerRelationships/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Features/TraineeNotes/", StringComparison.OrdinalIgnoreCase)
                => "Coaching",
            var path when path.StartsWith("LgymApi.Application/Features/DietPlans/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("LgymApi.Application/Features/Supplementation/", StringComparison.OrdinalIgnoreCase)
                => "Nutrition",
            _ => "Platform / Reference Data"
        };
    }

    private sealed record HostDependencyViolation(string SourceModule, string SourceSymbol, string ForbiddenTarget, string File, int Line)
    {
        public string IdentityKey => $"source-module:{SourceModule}|source:{SourceSymbol}|target:{ForbiddenTarget}";

        public override string ToString() => $"{File}:{Line} source-module:{SourceModule} source:{SourceSymbol} forbidden-target:{ForbiddenTarget}";
    }
}
