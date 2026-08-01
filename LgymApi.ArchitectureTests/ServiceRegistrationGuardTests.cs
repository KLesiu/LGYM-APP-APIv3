using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ServiceRegistrationGuardTests
{
    private static readonly string[] ValidRegistrationMethods =
    {
        "AddScoped",
        "AddSingleton",
        "AddTransient"
    };

    private static readonly CanonicalServiceRegistration[] WorkoutProgressContractRegistrations =
    {
        new("IWorkoutProgressReadWriteService", "WorkoutProgressReadWriteService", "WorkoutProgress"),
        new("IWorkoutProgressDashboardReadService", "WorkoutProgressDashboardReadService", "WorkoutProgress"),
        new("IWorkoutProgressRankingReadService", "WorkoutProgressRankingReadService", "WorkoutProgress"),
        new("ICompleteTrainingUseCase", "CompleteTrainingUseCase", "WorkoutProgress"),
        new("ITrainingHistoryReadService", "TrainingHistoryReadService", "WorkoutProgress"),
        new("IRankingAccountProfileReadService", "RankingAccountProfileReadService", "Identity")
    };

    [Test]
    public void Feature_Services_Should_Be_Registered_In_ServiceCollection()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var applicationFiles = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Application")
            .Concat(ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Identity"))
            .ToList();
        var serviceExtensionFiles = applicationFiles
            .Where(path => Path.GetFileName(path).EndsWith("ServiceCollectionExtensions.cs", StringComparison.Ordinal)
                || Path.GetFileName(path).Equals("IdentityModule.cs", StringComparison.Ordinal))
            .ToList();

        Assert.That(serviceExtensionFiles, Is.Not.Empty, "No Application ServiceCollectionExtensions files found for the DI guard test.");

        var serviceDeclarations = CollectServiceDeclarations(applicationFiles, parseOptions);
        var registrations = CollectRegistrations(serviceExtensionFiles, parseOptions);

        Assert.That(serviceDeclarations, Is.Not.Empty, "No concrete feature services detected.");
        Assert.That(registrations, Is.Not.Empty, "No service registrations detected in module-owned helper files.");

        var rootRegistrations = registrations
            .Where(registration => registration.Module is null)
            .ToList();

        Assert.That(
            rootRegistrations,
            Is.Empty,
            "Service registrations must live in module-owned ServiceCollectionExtensions files, not the project-root composition shim." + Environment.NewLine +
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
            "Duplicate service registrations were found across module-owned helper files." + Environment.NewLine +
            string.Join(Environment.NewLine, duplicateRegistrations.Select(registration => registration.ToString())));

        var missing = serviceDeclarations
            .Where(declaration => !registrations.Any(registration =>
                registration.Interface == declaration.Interface &&
                registration.Implementation == declaration.Implementation))
            .OrderBy(declaration => declaration.Interface, StringComparer.Ordinal)
            .ThenBy(declaration => declaration.Implementation, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "Every feature service must be registered in ServiceCollectionExtensions." + Environment.NewLine +
            string.Join(Environment.NewLine, missing.Select(m => m.ToString())));
    }

    [Test]
    public void Workout_Progress_Public_Contracts_Should_Be_Registered_Exactly_Once_By_Their_Owning_Module()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var applicationFiles = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Application")
            .Concat(ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Identity"))
            .ToList();
        var registrations = CollectRegistrations(
            applicationFiles.Where(path => Path.GetFileName(path).EndsWith("ServiceCollectionExtensions.cs", StringComparison.Ordinal)
                || Path.GetFileName(path).Equals("IdentityModule.cs", StringComparison.Ordinal)),
            parseOptions);

        var violations = WorkoutProgressContractRegistrations
            .Select(expected => new
            {
                Expected = expected,
                Matches = registrations.Where(registration =>
                    registration.Interface == expected.Interface &&
                    registration.Implementation == expected.Implementation).ToList()
            })
            .Where(entry => entry.Matches.Count != 1 || entry.Matches.Single().Module != entry.Expected.Module)
            .Select(entry => $"{entry.Expected.Interface} -> {entry.Expected.Implementation}: expected exactly one registration in {entry.Expected.Module}.")
            .ToList();

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    private static List<ServiceDeclaration> CollectServiceDeclarations(IEnumerable<string> sourceFiles, CSharpParseOptions parseOptions)
    {
        var declarations = new List<ServiceDeclaration>();

        foreach (var file in sourceFiles.Where(IsFeatureServicePath))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
            var root = tree.GetCompilationUnitRoot();

            foreach (var typeDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!IsConcreteService(typeDeclaration))
                {
                    continue;
                }

                var interfaceName = ResolveServiceInterface(typeDeclaration);
                if (interfaceName == null)
                {
                    continue;
                }

                declarations.Add(new ServiceDeclaration(interfaceName, typeDeclaration.Identifier.ValueText, file));
            }
        }

        return declarations;
    }

    private static List<ServiceRegistration> CollectRegistrations(IEnumerable<string> serviceExtensionFiles, CSharpParseOptions parseOptions)
    {
        var registrations = new List<ServiceRegistration>();

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

                if (genericName.TypeArgumentList.Arguments.Count < 2)
                {
                    continue;
                }

                registrations.Add(new ServiceRegistration(
                    NormalizeType(genericName.TypeArgumentList.Arguments[0]),
                    NormalizeType(genericName.TypeArgumentList.Arguments[1]),
                    module,
                    file));
            }
        }

        return registrations;
    }

    private static bool IsConcreteService(ClassDeclarationSyntax typeDeclaration)
    {
        if (!typeDeclaration.Identifier.ValueText.EndsWith("Service", StringComparison.Ordinal))
        {
            return false;
        }

        return !typeDeclaration.Modifiers.Any(modifier =>
            modifier.IsKind(SyntaxKind.AbstractKeyword) || modifier.IsKind(SyntaxKind.StaticKeyword));
    }

    private static bool IsFeatureServicePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (!normalized.Contains("/LgymApi.Application/", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains("/LgymApi.Identity/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var excludedSegments = new[]
        {
            "/Repositories/",
            "/Units/",
            "/Mapping/",
            "/Models/",
            "/Exceptions/",
            "/Properties/"
        };

        return !excludedSegments.Any(segment => normalized.Contains(segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveServiceInterface(ClassDeclarationSyntax typeDeclaration)
    {
        if (typeDeclaration.BaseList == null)
        {
            return null;
        }

        foreach (var baseType in typeDeclaration.BaseList.Types.OfType<SimpleBaseTypeSyntax>())
        {
            var identifier = baseType.Type.ToString();
            if (identifier.StartsWith('I') && identifier.EndsWith("Service", StringComparison.Ordinal))
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

    private static bool IsInBuildArtifacts(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ServiceDeclaration(string Interface, string Implementation, string SourceFile)
    {
        public override string ToString() => $"Missing registration: {Interface} -> {Implementation}";
    }

    private sealed record ServiceRegistration(string Interface, string Implementation, string? Module, string SourceFile)
    {
        public override string ToString()
            => $"{SourceFile} [{Module ?? "root"}]: {Interface} -> {Implementation}";
    }

    private sealed record CanonicalServiceRegistration(string Interface, string Implementation, string Module);
}
