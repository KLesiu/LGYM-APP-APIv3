using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class RepositoryRegistrationGuardTests
{
    private static readonly string[] ValidRegistrationMethods =
    {
        "AddScoped",
        "AddSingleton",
        "AddTransient"
    };

    [Test]
    public void Repositories_Should_Be_Registered_In_Infrastructure_ServiceCollection()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var infrastructureFiles = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Infrastructure");
        var repositoryFiles = infrastructureFiles
            .Where(path => Path.GetFileName(path).EndsWith("Repository.cs", StringComparison.Ordinal))
            .ToList();
        var serviceExtensionFiles = infrastructureFiles
            .Where(path => Path.GetFileName(path).EndsWith("ServiceCollectionExtensions.cs", StringComparison.Ordinal))
            .ToList();

        Assert.That(repositoryFiles, Is.Not.Empty, "No repository files found for DI guard test.");
        Assert.That(serviceExtensionFiles, Is.Not.Empty, "No Infrastructure ServiceCollectionExtensions files found for the DI guard test.");

        var repositoryDeclarations = CollectRepositoryDeclarations(repositoryFiles, parseOptions);
        var registrations = CollectRegistrations(serviceExtensionFiles, parseOptions);

        Assert.That(repositoryDeclarations, Is.Not.Empty, "No concrete repositories detected.");
        Assert.That(registrations, Is.Not.Empty, "No repository registrations detected in module-owned helper files.");

        var rootRegistrations = registrations
            .Where(registration => registration.Module is null)
            .ToList();

        Assert.That(
            rootRegistrations,
            Is.Empty,
            "Repository registrations must live in module-owned ServiceCollectionExtensions files, not the project-root composition shim." + Environment.NewLine +
            string.Join(Environment.NewLine, rootRegistrations.Select(registration => registration.ToString())));

        var duplicateRegistrations = registrations
            .GroupBy(registration => new { registration.Interface, registration.Implementation })
            .Where(group => group.Count() > 1)
            .Select(group => group.First())
            .OrderBy(registration => registration.Interface, StringComparer.Ordinal)
            .ThenBy(registration => registration.Implementation, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            duplicateRegistrations,
            Is.Empty,
            "Duplicate repository registrations were found across module-owned helper files." + Environment.NewLine +
            string.Join(Environment.NewLine, duplicateRegistrations.Select(registration => registration.ToString())));

        var missing = repositoryDeclarations
            .Where(declaration => !registrations.Any(registration =>
                registration.Interface == declaration.Interface &&
                registration.Implementation == declaration.Implementation))
            .OrderBy(declaration => declaration.Interface, StringComparer.Ordinal)
            .ThenBy(declaration => declaration.Implementation, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "Every repository must be registered in Infrastructure ServiceCollectionExtensions." + Environment.NewLine +
            string.Join(Environment.NewLine, missing.Select(m => m.ToString())));
    }

    private static List<RepositoryDeclaration> CollectRepositoryDeclarations(IEnumerable<string> repositoryFiles, CSharpParseOptions parseOptions)
    {
        var declarations = new List<RepositoryDeclaration>();

        foreach (var file in repositoryFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
            var root = tree.GetCompilationUnitRoot();

            foreach (var typeDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!IsConcreteRepository(typeDeclaration))
                {
                    continue;
                }

                var interfaceName = ResolveRepositoryInterface(typeDeclaration);
                if (interfaceName == null)
                {
                    continue;
                }

                declarations.Add(new RepositoryDeclaration(interfaceName, typeDeclaration.Identifier.ValueText, file));
            }
        }

        return declarations;
    }

    private static List<InfrastructureRegistration> CollectRegistrations(IEnumerable<string> serviceExtensionFiles, CSharpParseOptions parseOptions)
    {
        var registrations = new List<InfrastructureRegistration>();

        foreach (var file in serviceExtensionFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
            var root = tree.GetCompilationUnitRoot();
            var module = ArchitectureTestHelpers.GetServiceCollectionModuleName(file);

            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName })
                {
                    continue;
                }

                if (!ValidRegistrationMethods.Contains(genericName.Identifier.ValueText))
                {
                    continue;
                }

                if (genericName.TypeArgumentList.Arguments.Count >= 2)
                {
                    registrations.Add(new InfrastructureRegistration(
                        NormalizeType(genericName.TypeArgumentList.Arguments[0]),
                        NormalizeType(genericName.TypeArgumentList.Arguments[1]),
                        module,
                        file));
                    continue;
                }

                if (genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    var interfaceType = NormalizeType(genericName.TypeArgumentList.Arguments[0]);
                    var factoryImplementation = ExtractFactoryImplementation(invocation);
                    if (factoryImplementation != null)
                    {
                        registrations.Add(new InfrastructureRegistration(interfaceType, factoryImplementation, module, file));
                    }
                }
            }
        }

        return registrations;
    }

    private static bool IsConcreteRepository(ClassDeclarationSyntax typeDeclaration)
    {
        if (!typeDeclaration.Identifier.ValueText.EndsWith("Repository", StringComparison.Ordinal))
        {
            return false;
        }

        return !typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AbstractKeyword));
    }

    private static string? ResolveRepositoryInterface(ClassDeclarationSyntax typeDeclaration)
    {
        if (typeDeclaration.BaseList == null)
        {
            return null;
        }

        foreach (var baseType in typeDeclaration.BaseList.Types.OfType<SimpleBaseTypeSyntax>())
        {
            var identifier = baseType.Type.ToString();
            if (identifier.StartsWith('I') && identifier.EndsWith("Repository", StringComparison.Ordinal))
            {
                return NormalizeType(baseType.Type);
            }
        }

        return null;
    }

    private static string NormalizeType(TypeSyntax typeSyntax)
    {
        return typeSyntax
            .ToString()
            .Replace("global::", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string? ExtractFactoryImplementation(InvocationExpressionSyntax invocation)
    {
        var objectCreation = invocation.ArgumentList.Arguments
            .SelectMany(argument => argument.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            .FirstOrDefault();

        return objectCreation == null ? null : NormalizeType(objectCreation.Type);
    }

    private sealed record RepositoryDeclaration(string Interface, string Implementation, string SourceFile)
    {
        public override string ToString() => $"{SourceFile}: {Interface} -> {Implementation}";
    }

    private sealed record InfrastructureRegistration(string Interface, string Implementation, string? Module, string SourceFile)
    {
        public override string ToString() => $"{SourceFile} [{Module ?? "root"}]: {Interface} -> {Implementation}";
    }
}
