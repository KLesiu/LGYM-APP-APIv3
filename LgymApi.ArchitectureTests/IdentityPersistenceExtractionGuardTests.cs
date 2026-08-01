using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class IdentityPersistenceExtractionGuardTests
{
    private static readonly string[] RepositoryFiles =
    [
        "UserRepository.cs",
        "RoleRepository.cs",
        "UserExternalLoginRepository.cs",
        "PasswordResetTokenRepository.cs",
        "TutorialProgressRepository.cs"
    ];

    private static readonly string[] ProviderFiles =
    [
        "TokenService.cs",
        "GoogleTokenValidator.cs",
        "LegacyPasswordService.cs",
        "UserSessionStore.cs"
    ];

    private static readonly string[] ConfigurationTypes =
    [
        "UserEntityTypeConfiguration",
        "RoleEntityTypeConfiguration",
        "UserRoleEntityTypeConfiguration",
        "RoleClaimEntityTypeConfiguration",
        "PasswordResetTokenEntityTypeConfiguration",
        "UserExternalLoginEntityTypeConfiguration",
        "UserSessionEntityTypeConfiguration",
        "UserTutorialProgressEntityTypeConfiguration",
        "UserTutorialStepProgressEntityTypeConfiguration"
    ];

    private static readonly (string Service, string Implementation)[] ScopedRegistrations =
    [
        ("ITokenService", "TokenService"),
        ("IGoogleTokenValidator", "GoogleTokenValidator"),
        ("ILegacyPasswordService", "LegacyPasswordService"),
        ("IUserSessionStore", "UserSessionStore"),
        ("IUserRepository", "UserRepository"),
        ("IUserExternalLoginRepository", "UserExternalLoginRepository"),
        ("IPasswordResetTokenRepository", "PasswordResetTokenRepository"),
        ("IRoleRepository", "RoleRepository"),
        ("ITutorialProgressRepository", "TutorialProgressRepository")
    ];

    private static readonly (string Name, string Id)[] SeedIds =
    [
        ("UserRoleSeedId", "f124fe5f-9bf2-45df-bfd2-d5d6be920016"),
        ("AdminRoleSeedId", "1754c6f8-c021-41aa-b610-17088f9476f9"),
        ("TesterRoleSeedId", "f93f03af-ae11-4fd8-a60e-f970f89df6fb"),
        ("TrainerRoleSeedId", "8c1a3db8-72a3-47cc-b3de-f5347c6ae501"),
        ("AdminAccessClaimSeedId", "9dbfd057-cf88-4597-b668-2fdf16a2def6"),
        ("ManageUserRolesClaimSeedId", "97f7ea56-0032-4f18-8703-ab2d1485ad45"),
        ("ManageAppConfigClaimSeedId", "d12f9f84-48f4-4f4b-9614-843f31ea0f96"),
        ("ManageGlobalExercisesClaimSeedId", "27965bf4-ff55-4261-8f98-218ccf00e537"),
        ("TrainerAccessClaimSeedId", "a3b7c9d1-4e5f-6a7b-8c9d-0e1f2a3b4c5d")
    ];

    [Test]
    public void Identity_Persistence_Sources_Should_Be_Exclusive_Internal_And_Context_Bounded()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var identityRoot = Path.Combine(root, "LgymApi.Identity");
        var infrastructureRoot = Path.Combine(root, "LgymApi.Infrastructure");
        var sources = RepositoryFiles.Select(file => Path.Combine(identityRoot, "Persistence", "Repositories", file))
            .Concat(ProviderFiles.Select(file => Path.Combine(identityRoot, "Services", file)))
            .ToArray();

        Assert.That(sources.All(File.Exists), Is.True);
        Assert.That(RepositoryFiles.Select(file => Path.Combine(infrastructureRoot, "Repositories", file)).Where(File.Exists), Is.Empty);
        Assert.That(ProviderFiles.Select(file => Path.Combine(infrastructureRoot, "Services", file)).Where(File.Exists), Is.Empty);

        foreach (var sourcePath in sources)
        {
            var source = File.ReadAllText(sourcePath);
            var rootSyntax = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
            Assert.That(rootSyntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(type => type.Modifiers.Any(modifier => modifier.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)), Is.False, sourcePath);
            Assert.That(source, Does.Not.Contain("AppDbContext").And.Not.Contain("SaveChanges").And.Not.Contain("BeginTransaction").And.Not.Contain(".Database"), sourcePath);
        }

        foreach (var sourcePath in sources.Take(RepositoryFiles.Length).Append(Path.Combine(identityRoot, "Services", "UserSessionStore.cs")))
        {
            Assert.That(File.ReadAllText(sourcePath), Does.Contain("IIdentityPersistenceContext"), sourcePath);
        }
    }

    [Test]
    public void Identity_Registrar_Seeds_Packages_And_Di_Should_Retain_Their_Exact_Contracts()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var identityRoot = Path.Combine(root, "LgymApi.Identity");
        var registrar = File.ReadAllText(Path.Combine(identityRoot, "Persistence", "IdentityModelConfigurationRegistrar.cs"));
        var seed = File.ReadAllText(Path.Combine(identityRoot, "Persistence", "SeedData", "RoleSeedDataConfiguration.cs"));
        var publicSeedIds = File.ReadAllText(Path.Combine(identityRoot, "Contracts", "IdentitySeedIds.cs"));
        var identityProject = File.ReadAllText(Path.Combine(identityRoot, "LgymApi.Identity.csproj"));
        var infrastructureProject = File.ReadAllText(Path.Combine(root, "LgymApi.Infrastructure", "LgymApi.Infrastructure.csproj"));
        var registrations = File.ReadAllText(Path.Combine(identityRoot, "IdentityModule.cs"));

        Assert.That(ConfigurationTypes.Select(type => Path.Combine(identityRoot, "Persistence", "Configurations", type + ".cs")).All(File.Exists), Is.True);
        Assert.That(ExtractConfigurationTypes(registrar), Is.EqualTo(ConfigurationTypes));
        Assert.That(File.Exists(Path.Combine(root, "LgymApi.Infrastructure", "Data", "SeedData", "RoleSeedDataConfiguration.cs")), Is.False);
        EnsureSeedIds(seed, publicSeedIds);
        EnsureScopedRegistrations(registrations);

        foreach (var package in new[] { "Google.Apis.Auth", "Microsoft.EntityFrameworkCore.Relational", "Microsoft.Extensions.Http", "Microsoft.IdentityModel.Tokens", "Npgsql.EntityFrameworkCore.PostgreSQL", "System.IdentityModel.Tokens.Jwt" })
        {
            Assert.That(identityProject, Does.Contain($"Include=\"{package}\""));
        }
        foreach (var package in new[] { "Google.Apis.Auth", "Microsoft.IdentityModel.Tokens", "System.IdentityModel.Tokens.Jwt" })
        {
            Assert.That(infrastructureProject, Does.Not.Contain($"Include=\"{package}\""));
        }
    }

    [Test]
    public void Identity_Extraction_Guards_Should_Reject_Omitted_Duplicate_NonScoped_And_Changed_Seed_Fixtures()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => EnsureExactConfigurations(ConfigurationTypes[..^1]), Throws.InvalidOperationException);
            Assert.That(() => EnsureExactConfigurations([.. ConfigurationTypes, ConfigurationTypes[^1]]), Throws.InvalidOperationException);
            Assert.That(() => EnsureScopedRegistrations("services.AddSingleton<ITokenService, TokenService>();"), Throws.InvalidOperationException);
            Assert.That(() => EnsureSeedIds("internal static class RoleSeedDataConfiguration { public static readonly object UserRoleSeedId = ParseSeedId<Role>(IdentitySeedIds.UserRole); }", "public static class IdentitySeedIds { public const string UserRole = \"changed\"; }"), Throws.InvalidOperationException);
        });
    }

    private static IReadOnlyList<string> ExtractConfigurationTypes(string source)
        => CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Select(creation => creation.Type.ToString())
            .Where(type => type.EndsWith("EntityTypeConfiguration", StringComparison.Ordinal))
            .ToArray();

    private static void EnsureExactConfigurations(IReadOnlyList<string> actual)
    {
        if (!actual.SequenceEqual(ConfigurationTypes, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Identity configuration registrar order changed.");
        }
    }

    private static void EnsureSeedIds(string seedSource, string publicContractSource)
    {
        if (SeedIds.Any(seed =>
                !seedSource.Contains($"{seed.Name} = ParseSeedId", StringComparison.Ordinal)
                || !seedSource.Contains("IdentitySeedIds.", StringComparison.Ordinal)
                || !publicContractSource.Contains($"\"{seed.Id}\"", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Identity role or claim seed IDs changed.");
        }
    }

    private static void EnsureScopedRegistrations(string source)
    {
        var registrations = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax { TypeArgumentList.Arguments.Count: 2 } })
            .Select(invocation => (GenericNameSyntax)((MemberAccessExpressionSyntax)invocation.Expression).Name)
            .Select(name => (Method: name.Identifier.ValueText, Service: name.TypeArgumentList.Arguments[0].ToString(), Implementation: name.TypeArgumentList.Arguments[1].ToString()))
            .ToArray();

        foreach (var expected in ScopedRegistrations)
        {
            var matches = registrations.Where(registration => registration.Service == expected.Service && registration.Implementation == expected.Implementation).ToArray();
            if (matches.Length != 1 || matches[0].Method != "AddScoped")
            {
                throw new InvalidOperationException($"{expected.Service} must be registered once as scoped by IdentityModule.");
            }
        }
    }
}
