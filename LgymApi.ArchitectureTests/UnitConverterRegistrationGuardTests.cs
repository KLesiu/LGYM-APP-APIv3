using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class UnitConverterRegistrationGuardTests
{
    [Test]
    public void Unit_Strategies_And_Converters_Should_Be_Registered_In_ServiceCollection()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var unitFiles = ArchitectureTestHelpers
            .EnumerateProjectSourceFiles("LgymApi.Platform", "*.cs")
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Units{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var serviceExtensionFiles = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Platform")
            .Where(path => Path.GetFileName(path).EndsWith("ServiceCollectionExtensions.cs", StringComparison.Ordinal))
            .ToList();

        Assert.That(unitFiles, Is.Not.Empty, "No unit strategy/converter files found for the DI guard test.");
        Assert.That(serviceExtensionFiles, Is.Not.Empty, "No Platform ServiceCollectionExtensions files found for the DI guard test.");

        var requiredRegistrations = new List<UnitRegistration>();
        foreach (var file in unitFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
            var root = tree.GetCompilationUnitRoot();

            foreach (var typeDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AbstractKeyword))
                    && typeDeclaration.Identifier.ValueText.EndsWith("LinearUnitStrategy", StringComparison.Ordinal))
                {
                    requiredRegistrations.Add(new UnitRegistration(typeDeclaration.Identifier.ValueText, "LinearUnitStrategy", null, file));
                }

                if (!typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AbstractKeyword))
                    && typeDeclaration.Identifier.ValueText.EndsWith("UnitConverter", StringComparison.Ordinal))
                {
                    requiredRegistrations.Add(new UnitRegistration(typeDeclaration.Identifier.ValueText, "UnitConverter", null, file));
                }
            }
        }

        if (requiredRegistrations.Count == 0)
        {
            Assert.Pass("No unit strategies or converters detected – nothing to validate.");
            return;
        }

        var registrations = CollectRegistrations(serviceExtensionFiles, parseOptions);

        var rootRegistrations = registrations.Where(registration => registration.Module is null).ToList();
        Assert.That(
            rootRegistrations,
            Is.Empty,
            "Unit registrations must live in module-owned ServiceCollectionExtensions files, not the project-root composition shim." + Environment.NewLine +
            string.Join(Environment.NewLine, rootRegistrations.Select(registration => registration.ToString())));

        var duplicateRegistrations = registrations
            .GroupBy(registration => new { registration.TypeName, registration.Kind })
            .Where(group => group.Count() > 1)
            .Select(group => group.First())
            .OrderBy(registration => registration.TypeName, StringComparer.Ordinal)
            .ThenBy(registration => registration.Kind, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            duplicateRegistrations,
            Is.Empty,
            "Duplicate unit registrations were found across module-owned helper files." + Environment.NewLine +
            string.Join(Environment.NewLine, duplicateRegistrations.Select(registration => registration.ToString())));

        var missing = requiredRegistrations
            .Where(registration => !registrations.Any(candidate =>
                SimplifyTypeName(candidate.TypeName) == registration.TypeName &&
                candidate.Kind == registration.Kind))
            .OrderBy(registration => registration.TypeName, StringComparer.Ordinal)
            .ThenBy(registration => registration.Kind, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "Unit strategies and converters must be registered in ServiceCollectionExtensions." + Environment.NewLine +
            string.Join(Environment.NewLine, missing.Select(m => m.ToString())));
    }

    private static List<UnitRegistration> CollectRegistrations(IEnumerable<string> serviceExtensionFiles, CSharpParseOptions parseOptions)
    {
        var registrations = new List<UnitRegistration>();

        foreach (var file in serviceExtensionFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
            var root = tree.GetCompilationUnitRoot();
            var module = ArchitectureTestHelpers.GetServiceCollectionModuleName(file);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                {
                    continue;
                }

                if (memberAccess.Name is not GenericNameSyntax genericName)
                {
                    continue;
                }

                if (genericName.TypeArgumentList.Arguments.Count < 2)
                {
                    continue;
                }

                var implementationType = NormalizeType(genericName.TypeArgumentList.Arguments[1]);
                if (SimplifyTypeName(implementationType).EndsWith("LinearUnitStrategy", StringComparison.Ordinal))
                {
                    registrations.Add(new UnitRegistration(implementationType, "LinearUnitStrategy", module, file));
                }

                if (SimplifyTypeName(implementationType).EndsWith("UnitConverter", StringComparison.Ordinal))
                {
                    registrations.Add(new UnitRegistration(implementationType, "UnitConverter", module, file));
                }
            }
        }

        return registrations;
    }

    private static string NormalizeType(TypeSyntax typeSyntax)
    {
        return typeSyntax
            .ToString()
            .Replace("global::", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string SimplifyTypeName(string typeName)
    {
        var genericIndex = typeName.IndexOf('<');
        return genericIndex >= 0 ? typeName[..genericIndex] : typeName;
    }

    private static bool IsInBuildArtifacts(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LgymApi.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }

    private sealed record UnitRegistration(string TypeName, string Kind, string? Module, string SourceFile)
    {
        public override string ToString() => $"{SourceFile} [{Module ?? "root"}]: {Kind} -> {SimplifyTypeName(TypeName)}";
    }
}
