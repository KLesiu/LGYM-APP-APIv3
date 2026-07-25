using LgymApi.Domain.Entities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModulePersistenceOwnershipGuardTests
{
    private static readonly string[] ValidRegistrationMethods =
    {
        "AddScoped",
        "AddSingleton",
        "AddTransient"
    };

    private static readonly CanonicalRepositoryRegistration[] CanonicalRepositoryRegistrations =
    {
        new("IExerciseRepository", "ExerciseRepository", "WorkoutProgress"),
        new("ITrainingRepository", "TrainingRepository", "WorkoutProgress"),
        new("ITrainingExerciseScoreRepository", "TrainingExerciseScoreRepository", "WorkoutProgress"),
        new("IExerciseScoreRepository", "ExerciseScoreRepository", "WorkoutProgress"),
        new("IMeasurementRepository", "MeasurementRepository", "WorkoutProgress"),
        new("IGymRepository", "GymRepository", "WorkoutProgress"),
        new("IEloRegistryRepository", "EloRegistryRepository", "WorkoutProgress"),
        new("IMainRecordRepository", "MainRecordRepository", "WorkoutProgress"),
        new("IEmailNotificationLogRepository", "EmailNotificationLogRepository", "Notifications"),
        new("IEmailNotificationSubscriptionRepository", "EmailNotificationSubscriptionRepository", "Notifications"),
        new("IDietPlanPersistence", "DietPlanPersistenceRepository", "Nutrition"),
        new("ISupplementationPersistence", "SupplementationPersistenceRepository", "Nutrition")
    };

    [Test]
    public void Repositories_Configurations_And_Explicit_Registrar_Should_Stay_In_Their_Owning_Module()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var infrastructureFiles = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Infrastructure");

        var repositoryFiles = infrastructureFiles
            .Where(path => Path.GetFileName(path).EndsWith("Repository.cs", StringComparison.Ordinal))
            .ToList();
        var serviceExtensionFiles = infrastructureFiles
            .Where(path => Path.GetFileName(path).EndsWith("ServiceCollectionExtensions.cs", StringComparison.Ordinal))
            .ToList();
        var configurationFiles = infrastructureFiles
            .Where(path =>
                ArchitectureTestHelpers.NormalizePath(path).Contains("/Data/Configurations/", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(path).EndsWith("EntityTypeConfiguration.cs", StringComparison.Ordinal))
            .ToList();
        var registrarFiles = infrastructureFiles
            .Where(path => Path.GetFileName(path).Equals("AppDbContextEntityTypeConfigurationRegistrar.cs", StringComparison.Ordinal))
            .ToList();
        var appDbContextFiles = infrastructureFiles
            .Where(path => Path.GetFileName(path).Equals("AppDbContext.cs", StringComparison.Ordinal))
            .ToList();

        Assert.That(repositoryFiles, Is.Not.Empty, "No repository files found for the persistence ownership guard.");
        Assert.That(serviceExtensionFiles, Is.Not.Empty, "No Infrastructure ServiceCollectionExtensions files found for the persistence ownership guard.");
        Assert.That(configurationFiles, Is.Not.Empty, "No module-owned EF configuration files found for the persistence ownership guard.");
        Assert.That(registrarFiles, Has.Count.EqualTo(1), "The explicit AppDbContext entity-type registrar must exist exactly once in Infrastructure/Data/Configurations.");
        Assert.That(appDbContextFiles, Has.Count.EqualTo(1), "The shared production AppDbContext root must exist exactly once.");

        var repositoryDeclarations = CollectRepositoryDeclarations(repositoryFiles, parseOptions);
        var registrations = CollectRegistrations(serviceExtensionFiles, parseOptions);
        var configurationDeclarations = CollectConfigurationDeclarations(configurationFiles, parseOptions);
        var registeredConfigurations = CollectRegisteredConfigurations(registrarFiles.Single(), parseOptions);

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

        var canonicalRepositoryRegistrationViolations = FindCanonicalRepositoryRegistrationViolations(
            registrations,
            CanonicalRepositoryRegistrations);

        Assert.That(
            canonicalRepositoryRegistrationViolations,
            Is.Empty,
            "Canonical repository registrations must be scoped exactly once in their owning module." + Environment.NewLine +
            string.Join(Environment.NewLine, canonicalRepositoryRegistrationViolations.Select(violation => violation.ToString())));

        var missingRepositories = repositoryDeclarations
            .Where(declaration => !registrations.Any(registration =>
                registration.Interface == declaration.Interface &&
                registration.Implementation == declaration.Implementation))
            .OrderBy(declaration => declaration.Interface, StringComparer.Ordinal)
            .ThenBy(declaration => declaration.Implementation, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            missingRepositories,
            Is.Empty,
            "Every repository must be registered in Infrastructure ServiceCollectionExtensions." + Environment.NewLine +
            string.Join(Environment.NewLine, missingRepositories.Select(m => m.ToString())));

        var unownedConfigurations = configurationDeclarations
            .Where(configuration => configuration.Module is null)
            .ToList();

        Assert.That(
            unownedConfigurations,
            Is.Empty,
            "EF Core configuration files must stay under Data/Configurations/<Module>/ and not leak into alternate roots." + Environment.NewLine +
            string.Join(Environment.NewLine, unownedConfigurations.Select(configuration => configuration.ToString())));

        var configurationsWithoutCatalogOwners = configurationDeclarations
            .Where(configuration => configuration.CatalogOwner is null)
            .ToList();

        Assert.That(
            configurationsWithoutCatalogOwners,
            Is.Empty,
            "EF Core configuration entities must resolve through PersistedEntityOwnershipCatalog." + Environment.NewLine +
            string.Join(Environment.NewLine, configurationsWithoutCatalogOwners.Select(configuration => configuration.ToString())));

        var configurationsOutsideCatalogOwners = configurationDeclarations
            .Where(configuration => configuration.CatalogOwner is not null && configuration.Module != GetConfigurationModule(configuration.CatalogOwner, configuration.ConfigurationType))
            .ToList();

        Assert.That(
            configurationsOutsideCatalogOwners,
            Is.Empty,
            "EF Core configurations must remain under their persisted entity catalog owner module." + Environment.NewLine +
            string.Join(Environment.NewLine, configurationsOutsideCatalogOwners.Select(configuration => configuration.ToString())));

        var staleRegistrarEntries = registeredConfigurations
            .Where(typeName => !configurationDeclarations.Any(configuration =>
                string.Equals(configuration.ConfigurationType, typeName, StringComparison.Ordinal)))
            .OrderBy(typeName => typeName, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            staleRegistrarEntries,
            Is.Empty,
            "AppDbContextEntityTypeConfigurationRegistrar must only register module-owned configuration types that still exist." + Environment.NewLine +
            string.Join(Environment.NewLine, staleRegistrarEntries));

        var missingRegistrarEntries = configurationDeclarations
            .Where(configuration => !registeredConfigurations.Contains(configuration.ConfigurationType, StringComparer.Ordinal))
            .OrderBy(configuration => configuration.ConfigurationType, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            missingRegistrarEntries,
            Is.Empty,
            "Every module-owned EF Core configuration must be explicitly listed in AppDbContextEntityTypeConfigurationRegistrar." + Environment.NewLine +
            string.Join(Environment.NewLine, missingRegistrarEntries.Select(configuration => configuration.ToString())));

        var appDbContextContent = File.ReadAllText(appDbContextFiles.Single());
        Assert.That(
            appDbContextContent,
            Does.Contain("AppDbContextEntityTypeConfigurationRegistrar.Apply(modelBuilder);"),
            "The shared AppDbContext root must apply the explicit registrar instead of discovering module conventions by scanning.");

        var ownershipMessage = BuildOwnershipViolationMessage(
            "Identity & Accounts",
            "root composition shim",
            "IUserRepository -> UserRepository",
            "LgymApi.Infrastructure/ServiceCollectionExtensions.cs");

        Assert.Multiple(() =>
        {
            Assert.That(ownershipMessage, Does.Contain("Identity & Accounts"));
            Assert.That(ownershipMessage, Does.Contain("root composition shim"));
            Assert.That(ownershipMessage, Does.Contain("IUserRepository -> UserRepository"));
            Assert.That(ownershipMessage, Does.Contain("LgymApi.Infrastructure/ServiceCollectionExtensions.cs"));
        });
    }

    [TestCaseSource(nameof(InvalidCanonicalRepositoryRegistrationFixtures))]
    public void Canonical_Repository_Registration_Guard_Should_Reject_Invalid_Fixtures(
        object fixtureValue)
    {
        var fixture = (CanonicalRepositoryRegistrationFixture)fixtureValue;
        var violations = FindCanonicalRepositoryRegistrationViolations(
            fixture.Registrations,
            new[] { fixture.ExpectedRegistration });

        Assert.That(violations, Has.Count.EqualTo(1));
        Assert.That(violations.Single().ToString(), Is.EqualTo(fixture.ExpectedDiagnostic));
    }

    [TestCaseSource(nameof(CanonicalConfigurationCases))]
    public void Configuration_Should_Reside_Under_Its_Catalog_Owner(string configurationType, Type entityType)
    {
        var infrastructureFiles = ArchitectureTestHelpers.EnumerateProjectSourceFiles("LgymApi.Infrastructure");
        var configurationFiles = infrastructureFiles.Where(path =>
            ArchitectureTestHelpers.NormalizePath(path).Contains("/Data/Configurations/", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(path).EndsWith("EntityTypeConfiguration.cs", StringComparison.Ordinal));
        var configurations = CollectConfigurationDeclarations(
            configurationFiles,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));
        var configuration = configurations.Single(item => item.ConfigurationType == configurationType);
        var expectedOwner = PersistedEntityOwnershipCatalog.Entries.Single(entry => entry.EntityType == entityType).Owner;
        var expectedModule = GetConfigurationModule(expectedOwner, configurationType);

        Assert.That(configuration.Module, Is.EqualTo(expectedModule), $"{configuration}; catalog owner: {expectedOwner}");
    }

    private static IEnumerable<TestCaseData> CanonicalConfigurationCases()
    {
        yield return new TestCaseData("UserSessionEntityTypeConfiguration", typeof(UserSession));
        yield return new TestCaseData("UserTutorialProgressEntityTypeConfiguration", typeof(UserTutorialProgress));
        yield return new TestCaseData("UserTutorialStepProgressEntityTypeConfiguration", typeof(UserTutorialStepProgress));
        yield return new TestCaseData("AppConfigEntityTypeConfiguration", typeof(AppConfig));
        yield return new TestCaseData("ExerciseEntityTypeConfiguration", typeof(Exercise));
        yield return new TestCaseData("ExerciseTranslationEntityTypeConfiguration", typeof(ExerciseTranslation));
    }

    private static IEnumerable<TestCaseData> InvalidCanonicalRepositoryRegistrationFixtures()
    {
        var eloRegistration = new CanonicalRepositoryRegistration(
            "IEloRegistryRepository",
            "EloRegistryRepository",
            "WorkoutProgress");

        yield return new TestCaseData(new CanonicalRepositoryRegistrationFixture(
                eloRegistration,
                new[]
                {
                    new InfrastructureRegistration(
                        "IEloRegistryRepository",
                        "EloRegistryRepository",
                        "Identity",
                        "AddScoped",
                        "IdentityServiceCollectionExtensions.cs")
                },
                "IEloRegistryRepository -> EloRegistryRepository: expected AddScoped in WorkoutProgress; actual AddScoped in Identity."))
            .SetName("Canonical_Repository_Registration_Guard_Should_Reject_Old_Module_Fixture");

        yield return new TestCaseData(new CanonicalRepositoryRegistrationFixture(
                eloRegistration,
                new[]
                {
                    new InfrastructureRegistration(
                        "IEloRegistryRepository",
                        "EloRegistryRepository",
                        "WorkoutProgress",
                        "AddScoped",
                        "WorkoutProgressServiceCollectionExtensions.cs"),
                    new InfrastructureRegistration(
                        "IEloRegistryRepository",
                        "EloRegistryRepository",
                        "WorkoutProgress",
                        "AddScoped",
                        "DuplicateWorkoutProgressServiceCollectionExtensions.cs")
                },
                "IEloRegistryRepository -> EloRegistryRepository: expected exactly one AddScoped registration in WorkoutProgress; found 2 registrations in WorkoutProgress."))
            .SetName("Canonical_Repository_Registration_Guard_Should_Reject_Duplicate_Fixture");
    }

    private static string GetConfigurationModule(string catalogOwner, string configurationType)
    {
        return catalogOwner switch
        {
            PersistedEntityOwnershipCatalog.IdentityModuleName => "Identity",
            PersistedEntityOwnershipCatalog.NotificationsModuleName => "Notifications",
            PersistedEntityOwnershipCatalog.ReportingModuleName => "Reporting",
            PersistedEntityOwnershipCatalog.TrainingPlanningModuleName => "TrainingPlanning",
            PersistedEntityOwnershipCatalog.PlatformModuleName when configurationType == "AppConfigEntityTypeConfiguration" => "ReferenceData",
            PersistedEntityOwnershipCatalog.PlatformModuleName => "Platform",
            PersistedEntityOwnershipCatalog.WorkoutProgressModuleName => "WorkoutProgress",
            PersistedEntityOwnershipCatalog.CoachingModuleName => "Coaching",
            PersistedEntityOwnershipCatalog.NutritionModuleName => "Nutrition",
            _ => throw new InvalidOperationException($"No physical configuration-folder mapping exists for catalog owner '{catalogOwner}'.")
        };
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

    private static List<CanonicalRepositoryRegistrationViolation> FindCanonicalRepositoryRegistrationViolations(
        IEnumerable<InfrastructureRegistration> registrations,
        IEnumerable<CanonicalRepositoryRegistration> expectedRegistrations)
    {
        var violations = new List<CanonicalRepositoryRegistrationViolation>();

        foreach (var expected in expectedRegistrations)
        {
            var matches = registrations
                .Where(registration =>
                    registration.Interface == expected.Interface &&
                    registration.Implementation == expected.Implementation)
                .ToList();

            if (matches.Count != 1)
            {
                var actualModules = matches.Count == 0
                    ? "no module"
                    : string.Join(
                        ", ",
                        matches
                            .Select(registration => registration.Module ?? "root")
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(module => module, StringComparer.Ordinal));
                violations.Add(CanonicalRepositoryRegistrationViolation.ForCount(expected, matches.Count, actualModules));
                continue;
            }

            var registration = matches.Single();
            if (registration.Module != expected.Module || registration.RegistrationMethod != "AddScoped")
            {
                violations.Add(CanonicalRepositoryRegistrationViolation.ForPlacement(expected, registration));
            }
        }

        return violations;
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
                        genericName.Identifier.ValueText,
                        file));
                    continue;
                }

                if (genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    var interfaceType = NormalizeType(genericName.TypeArgumentList.Arguments[0]);
                    var factoryImplementation = ExtractFactoryImplementation(invocation);
                    if (factoryImplementation != null)
                    {
                        registrations.Add(new InfrastructureRegistration(
                            interfaceType,
                            factoryImplementation,
                            module,
                            genericName.Identifier.ValueText,
                            file));
                    }
                }
            }
        }

        return registrations;
    }

    private static List<ConfigurationDeclaration> CollectConfigurationDeclarations(IEnumerable<string> configurationFiles, CSharpParseOptions parseOptions)
    {
        var declarations = new List<ConfigurationDeclaration>();

        foreach (var file in configurationFiles)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), parseOptions, file);
            var root = tree.GetCompilationUnitRoot();
            var module = ArchitectureTestHelpers.GetInfrastructureConfigurationModuleName(file);

            foreach (var typeDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!IsConcreteEntityTypeConfiguration(typeDeclaration))
                {
                    continue;
                }

                var configuredEntity = ResolveConfiguredEntityType(typeDeclaration);
                string? catalogOwner = null;
                if (configuredEntity != null &&
                    ArchitectureTestHelpers.TryGetPersistedEntityOwner(configuredEntity, out var resolvedCatalogOwner))
                {
                    catalogOwner = resolvedCatalogOwner;
                }

                declarations.Add(new ConfigurationDeclaration(
                    module,
                    typeDeclaration.Identifier.ValueText,
                    configuredEntity,
                    catalogOwner,
                    file));
            }
        }

        return declarations;
    }

    private static HashSet<string> CollectRegisteredConfigurations(string registrarFile, CSharpParseOptions parseOptions)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(registrarFile), parseOptions, registrarFile);
        var root = tree.GetCompilationUnitRoot();

        return root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Select(objectCreation => NormalizeType(objectCreation.Type))
            .Where(typeName => typeName.EndsWith("EntityTypeConfiguration", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string BuildOwnershipViolationMessage(string ownerModule, string nonOwnerModule, string detail, string alternateRoot)
    {
        return "Module persistence ownership failed:" + Environment.NewLine +
               $"Owner module: {ownerModule}" + Environment.NewLine +
               $"Non-owner: {nonOwnerModule}" + Environment.NewLine +
               $"Detail: {detail}" + Environment.NewLine +
               $"Alternate root: {alternateRoot}";
    }

    private static bool IsConcreteRepository(ClassDeclarationSyntax typeDeclaration)
    {
        if (!typeDeclaration.Identifier.ValueText.EndsWith("Repository", StringComparison.Ordinal))
        {
            return false;
        }

        return !typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AbstractKeyword));
    }

    private static bool IsConcreteEntityTypeConfiguration(ClassDeclarationSyntax typeDeclaration)
    {
        if (!typeDeclaration.Identifier.ValueText.EndsWith("EntityTypeConfiguration", StringComparison.Ordinal))
        {
            return false;
        }

        return !typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AbstractKeyword));
    }

    private static string? ResolveConfiguredEntityType(ClassDeclarationSyntax configurationType)
    {
        return configurationType.BaseList?.Types
            .Select(baseType => baseType.Type)
            .OfType<GenericNameSyntax>()
            .SingleOrDefault(type => type.Identifier.ValueText == "IEntityTypeConfiguration")
            ?.TypeArgumentList.Arguments.SingleOrDefault()
            ?.ToString();
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

    private sealed record CanonicalRepositoryRegistration(string Interface, string Implementation, string Module);

    private sealed record CanonicalRepositoryRegistrationFixture(
        CanonicalRepositoryRegistration ExpectedRegistration,
        IReadOnlyList<InfrastructureRegistration> Registrations,
        string ExpectedDiagnostic);

    private sealed record CanonicalRepositoryRegistrationViolation(string Diagnostic)
    {
        public static CanonicalRepositoryRegistrationViolation ForCount(
            CanonicalRepositoryRegistration expected,
            int count,
            string actualModules)
        {
            return new CanonicalRepositoryRegistrationViolation(
                $"{expected.Interface} -> {expected.Implementation}: expected exactly one AddScoped registration in {expected.Module}; found {count} registrations in {actualModules}.");
        }

        public static CanonicalRepositoryRegistrationViolation ForPlacement(
            CanonicalRepositoryRegistration expected,
            InfrastructureRegistration actual)
        {
            return new CanonicalRepositoryRegistrationViolation(
                $"{expected.Interface} -> {expected.Implementation}: expected AddScoped in {expected.Module}; actual {actual.RegistrationMethod} in {actual.Module ?? "root"}.");
        }

        public override string ToString() => Diagnostic;
    }

    private sealed record InfrastructureRegistration(
        string Interface,
        string Implementation,
        string? Module,
        string RegistrationMethod,
        string SourceFile)
    {
        public override string ToString() => $"{SourceFile} [{Module ?? "root"}; {RegistrationMethod}]: {Interface} -> {Implementation}";
    }

    private sealed record ConfigurationDeclaration(
        string? Module,
        string ConfigurationType,
        string? ConfiguredEntity,
        string? CatalogOwner,
        string SourceFile)
    {
        public override string ToString()
            => $"{SourceFile} [{Module ?? "root"}; catalog owner: {CatalogOwner ?? "unmapped"}]: {ConfigurationType} -> {ConfiguredEntity ?? "unresolved entity"}";
    }
}
