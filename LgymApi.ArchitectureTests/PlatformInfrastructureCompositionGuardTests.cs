using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class PlatformInfrastructureCompositionGuardTests
{
    private static readonly string[] ExpectedPrivateHelpers =
    [
        "AddPlatformPersistence",
        "AddPlatformBackgroundRuntime",
        "AddPlatformPagination",
        "AddPlatformReliabilityDispatcher",
        "AddPlatformUnitOfWork",
        "AddReferenceDataInfrastructure",
        "AddPlatformReliabilityRepositories"
    ];

    [Test]
    public void PlatformFacade_Should_Compose_Required_Private_Helpers_And_Keep_Its_Stable_Signature()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var facadePath = Path.Combine(repoRoot, "LgymApi.Infrastructure", "PlatformServiceCollectionExtensions.cs");
        var facade = ParseFile(facadePath);
        var method = facade.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == "AddPlatformServices");

        Assert.Multiple(() =>
        {
            Assert.That(method.Modifiers.Select(modifier => modifier.ValueText), Is.SupersetOf(["public", "static"]));
            Assert.That(method.ParameterList.Parameters.Select(parameter => parameter.Type?.ToString()),
                Is.EqualTo(["IServiceCollection", "IConfiguration", "bool", "bool", "bool"]));
            Assert.That(method.ParameterList.Parameters[3].Default?.Value.ToString(), Is.EqualTo("false"));
            Assert.That(method.ParameterList.Parameters[4].Default?.Value.ToString(), Is.EqualTo("false"));
            Assert.That(method.ReturnType.ToString(), Is.EqualTo("IServiceCollection"));
            Assert.That(ExtractInvocationNames(method), Is.EqualTo(ExpectedPrivateHelpers));
        });

        foreach (var (fileName, helperName) in RequiredPrivateHelperLocations())
        {
            var helperPath = Path.Combine(repoRoot, "LgymApi.Infrastructure", fileName);
            Assert.That(File.Exists(helperPath), Is.True, $"Missing private platform helper source '{fileName}'.");

            var helper = ParseFile(helperPath).DescendantNodes().OfType<MethodDeclarationSyntax>()
                .SingleOrDefault(candidate => candidate.Identifier.ValueText == helperName);
            Assert.That(helper, Is.Not.Null, $"Missing private helper '{helperName}' in '{fileName}'.");
            Assert.That(helper!.Modifiers.Select(modifier => modifier.ValueText), Is.SupersetOf(["private", "static"]));
        }
    }

    [Test]
    public void PrivateHelperComposition_Fixture_Should_Reject_An_Omitted_Helper()
    {
        var omitted = ExpectedPrivateHelpers.Where(name => name != "AddPlatformPagination").ToArray();

        Assert.That(
            () => AssertExactPrivateHelperComposition(omitted),
            Throws.InvalidOperationException.With.Message.Contains("AddPlatformPagination"));
    }

    [Test]
    public void AppConfig_Configuration_Should_Stay_At_Its_Fixed_ReferenceData_Registrar_Ordinal()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var configurationPath = Path.Combine(
            repoRoot,
            "LgymApi.Infrastructure",
            "Data",
            "Configurations",
            "ReferenceData",
            "AppConfigEntityTypeConfiguration.cs");
        var registrarPath = Path.Combine(
            repoRoot,
            "LgymApi.Infrastructure",
            "Data",
            "Configurations",
            "AppDbContextEntityTypeConfigurationRegistrar.cs");

        Assert.That(File.Exists(configurationPath), Is.True);
        AssertAppConfigRegistrarPlacement(ExtractRegistrarEntries(ParseFile(registrarPath)));
    }

    [Test]
    public void AppConfig_Registrar_Fixture_Should_Reject_An_Altered_Ordinal()
    {
        var entries = PersistenceIdentityContract.RegistrarConfigurationTypes.ToList();
        entries.Remove("AppConfigEntityTypeConfiguration");
        entries.Insert(20, "AppConfigEntityTypeConfiguration");

        Assert.That(
            () => AssertAppConfigRegistrarPlacement(entries),
            Throws.InvalidOperationException.With.Message.Contains("ordinal 19"));
    }

    private static void AssertExactPrivateHelperComposition(IReadOnlyList<string> actual)
    {
        if (!actual.SequenceEqual(ExpectedPrivateHelpers, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Platform facade helper composition changed. Expected: {string.Join(", ", ExpectedPrivateHelpers)}. " +
                $"Actual: {string.Join(", ", actual)}.");
        }
    }

    private static void AssertAppConfigRegistrarPlacement(IReadOnlyList<string> entries)
    {
        const int appConfigOrdinal = 19;
        if (entries.Count != 48 ||
            entries.ElementAtOrDefault(appConfigOrdinal) != "AppConfigEntityTypeConfiguration" ||
            entries.ElementAtOrDefault(appConfigOrdinal - 1) != "EloRegistryEntityTypeConfiguration" ||
            entries.ElementAtOrDefault(appConfigOrdinal + 1) != "TrainerInvitationEntityTypeConfiguration")
        {
            throw new InvalidOperationException("AppConfigEntityTypeConfiguration must remain at registrar ordinal 19.");
        }
    }

    private static IReadOnlyList<string> ExtractInvocationNames(MethodDeclarationSyntax method)
    {
        var names = method.Body!.Statements
            .Select(statement => statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault())
            .Where(invocation => invocation is not null)
            .Select(invocation => invocation!.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                _ => string.Empty
            })
            .Where(name => ExpectedPrivateHelpers.Contains(name, StringComparer.Ordinal))
            .ToList();

        return names;
    }

    private static IReadOnlyList<string> ExtractRegistrarEntries(CompilationUnitSyntax root)
    {
        return root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Select(creation => creation.Type.ToString())
            .Where(name => name.EndsWith("EntityTypeConfiguration", StringComparison.Ordinal))
            .ToList();
    }

    private static IEnumerable<(string FileName, string HelperName)> RequiredPrivateHelperLocations()
    {
        yield return ("PlatformPersistenceServiceCollectionExtensions.cs", "AddPlatformPersistence");
        yield return ("PlatformPersistenceServiceCollectionExtensions.cs", "AddPlatformUnitOfWork");
        yield return ("PlatformBackgroundRuntimeServiceCollectionExtensions.cs", "AddPlatformBackgroundRuntime");
        yield return ("PlatformPaginationServiceCollectionExtensions.cs", "AddPlatformPagination");
        yield return ("PlatformReliabilityServiceCollectionExtensions.cs", "AddPlatformReliabilityDispatcher");
        yield return ("PlatformReliabilityServiceCollectionExtensions.cs", "AddPlatformReliabilityRepositories");
        yield return ("ReferenceDataServiceCollectionExtensions.cs", "AddReferenceDataInfrastructure");
    }

    private static CompilationUnitSyntax ParseFile(string path)
    {
        return CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetCompilationUnitRoot();
    }
}
