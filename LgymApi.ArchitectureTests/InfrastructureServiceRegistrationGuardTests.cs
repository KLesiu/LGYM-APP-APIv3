using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class InfrastructureServiceRegistrationGuardTests
{
    private static readonly string[] ValidRegistrationMethods =
    {
        "AddScoped",
        "AddSingleton",
        "AddTransient"
    };

    [Test]
    public void InfrastructureServiceRegistration_ConcreteInfrastructureServices_Should_Be_Registered_In_CompositionRoot()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var infrastructureFiles = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Infrastructure");
        var serviceExtensionFiles = infrastructureFiles
            .Where(path => Path.GetFileName(path).EndsWith("ServiceCollectionExtensions.cs", StringComparison.Ordinal))
            .ToList();

        var concreteServices = CollectConcreteInfrastructureServices(infrastructureFiles, parseOptions);
        var registrations = CollectRegistrations(serviceExtensionFiles, parseOptions);

        Assert.That(concreteServices, Is.Not.Empty, "No concrete infrastructure services detected for DI registration guard.");
        Assert.That(registrations, Is.Not.Empty, "No infrastructure registrations detected in module-owned helper files.");

        var rootRegistrations = registrations
            .Where(registration => registration.Module is null)
            .ToList();

        Assert.That(
            rootRegistrations,
            Is.Empty,
            "Infrastructure registrations must live in module-owned ServiceCollectionExtensions files, not the project-root composition shim." + Environment.NewLine +
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
            "Duplicate infrastructure registrations were found across module-owned helper files." + Environment.NewLine +
            string.Join(Environment.NewLine, duplicateRegistrations.Select(registration => registration.ToString())));

        var missing = concreteServices
            .Where(serviceName => !registrations.Any(registration => SimplifyTypeName(registration.Implementation) == serviceName))
            .OrderBy(serviceName => serviceName, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "Every concrete infrastructure service must be registered in Infrastructure ServiceCollectionExtensions." + Environment.NewLine +
            string.Join(Environment.NewLine, missing.Select(serviceName => $"Missing infrastructure service registration: {serviceName}")));
    }

    [Test]
    public void NotificationsEmailRegistration_Factory_Interface_Registration_Should_Not_Cause_False_Positive()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var serviceExtensionFiles = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Notifications")
            .Where(path => Path.GetFileName(path).Equals("EmailServiceCollectionExtensions.cs", StringComparison.Ordinal))
            .ToList();

        var registrations = CollectRegistrations(serviceExtensionFiles, parseOptions);

        var registrationCandidates = CollectRegistrationCandidates(serviceExtensionFiles, parseOptions).ToList();
        Assert.That(
            registrationCandidates.Any(candidate =>
                candidate.CandidateTypeName.Equals("IEmailSender", StringComparison.Ordinal) &&
                candidate.ArgumentCount == 1),
            Is.True,
            "Fixture assumption failed: expected AddScoped<IEmailSender>(sp => ...) factory registration in the Notifications email service collection extension.");

        var concreteRegistrations = registrations
            .Select(registration => SimplifyTypeName(registration.Implementation))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(concreteRegistrations.Contains("SmtpEmailSender"), Is.True, "Expected concrete self-registration for SmtpEmailSender.");
            Assert.That(concreteRegistrations.Contains("DummyEmailSender"), Is.True, "Expected concrete self-registration for DummyEmailSender.");
            Assert.That(concreteRegistrations.Contains("IEmailSender"), Is.False, "Factory interface registration must not be treated as concrete service registration.");
        });
    }

    private static HashSet<string> CollectConcreteInfrastructureServices(IEnumerable<string> sourceFiles, CSharpParseOptions parseOptions)
    {
        var declarations = new HashSet<string>(StringComparer.Ordinal);
        var allowedSegments = new[] { "\\Services\\", "\\Pagination\\", "\\UnitOfWork\\" };
        var excludedTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "EfUnitOfWorkTransaction",
            "NoOpUnitOfWorkTransaction",
            "FilterToGridifyAdapter",
            "WhitelistPolicy"
        };

        foreach (var file in sourceFiles)
        {
            var normalizedPath = file.Replace('/', '\\');
            if (!allowedSegments.Any(segment => normalizedPath.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
            var root = tree.GetCompilationUnitRoot();

            foreach (var typeDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!IsConcreteService(typeDeclaration))
                {
                    continue;
                }

                if (excludedTypeNames.Contains(typeDeclaration.Identifier.ValueText))
                {
                    continue;
                }

                declarations.Add(typeDeclaration.Identifier.ValueText);
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
            var aliases = CollectAliasMap(root);
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

                if (genericName.TypeArgumentList.Arguments.Count < 2)
                {
                    if (genericName.TypeArgumentList.Arguments.Count == 1 && invocation.ArgumentList.Arguments.Count == 0)
                    {
                        var selfType = ResolveTypeName(genericName.TypeArgumentList.Arguments[0], aliases);
                        registrations.Add(new InfrastructureRegistration(selfType, selfType, module, file));
                        continue;
                    }

                    if (genericName.TypeArgumentList.Arguments.Count == 1 && invocation.ArgumentList.Arguments.Count > 0)
                    {
                        var interfaceType = ResolveTypeName(genericName.TypeArgumentList.Arguments[0], aliases);
                        var factoryImplementation = ExtractFactoryImplementation(invocation, aliases);
                        if (factoryImplementation != null)
                        {
                            registrations.Add(new InfrastructureRegistration(interfaceType, factoryImplementation, module, file));
                        }
                    }

                    continue;
                }

                registrations.Add(new InfrastructureRegistration(
                    ResolveTypeName(genericName.TypeArgumentList.Arguments[0], aliases),
                    ResolveTypeName(genericName.TypeArgumentList.Arguments[1], aliases),
                    module,
                    file));
            }
        }

        return registrations;
    }

    private static IEnumerable<RegistrationCandidate> CollectRegistrationCandidates(IEnumerable<string> serviceExtensionFiles, CSharpParseOptions parseOptions)
    {
        foreach (var file in serviceExtensionFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
            var root = tree.GetCompilationUnitRoot();
            var aliases = CollectAliasMap(root);

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

                var arguments = genericName.TypeArgumentList.Arguments;
                if (arguments.Count == 1)
                {
                    yield return new RegistrationCandidate(ResolveTypeName(arguments[0], aliases), 1);
                    continue;
                }

                if (arguments.Count >= 2)
                {
                    yield return new RegistrationCandidate(ResolveTypeName(arguments[1], aliases), arguments.Count);
                }
            }
        }
    }

    private static bool IsConcreteService(ClassDeclarationSyntax typeDeclaration)
    {
        return !typeDeclaration.Modifiers.Any(modifier =>
            modifier.IsKind(SyntaxKind.AbstractKeyword) || modifier.IsKind(SyntaxKind.StaticKeyword));
    }

    private static Dictionary<string, string> CollectAliasMap(CompilationUnitSyntax root)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var usingDirective in root.Usings)
        {
            if (usingDirective.Alias == null)
            {
                continue;
            }

            aliases[usingDirective.Alias.Name.Identifier.ValueText] = usingDirective.Name.ToString()
                .Replace("global::", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal);
        }

        return aliases;
    }

    private static string ResolveTypeName(TypeSyntax typeSyntax, IReadOnlyDictionary<string, string> aliases)
    {
        var normalized = NormalizeType(typeSyntax);
        return aliases.TryGetValue(normalized, out var aliasTarget) ? aliasTarget : normalized;
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
        var simple = genericIndex >= 0 ? typeName[..genericIndex] : typeName;
        var lastNamespaceIndex = simple.LastIndexOf('.');
        return lastNamespaceIndex >= 0 ? simple[(lastNamespaceIndex + 1)..] : simple;
    }

    private static string? ExtractFactoryImplementation(InvocationExpressionSyntax invocation, IReadOnlyDictionary<string, string> aliases)
    {
        var objectCreation = invocation.ArgumentList.Arguments
            .SelectMany(argument => argument.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
            .FirstOrDefault();

        return objectCreation == null ? null : ResolveTypeName(objectCreation.Type, aliases);
    }

    private sealed record InfrastructureRegistration(string Interface, string Implementation, string? Module, string SourceFile)
    {
        public override string ToString() => $"{SourceFile} [{Module ?? "root"}]: {Interface} -> {Implementation}";
    }

    private sealed record RegistrationCandidate(string CandidateTypeName, int ArgumentCount);
}
