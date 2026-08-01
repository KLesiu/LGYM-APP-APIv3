namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class PlatformReferenceDataBoundaryDocumentationTests
{
    private const string ConcernPrefix = "issue393.concern.";
    private const string SubBoundaryPrefix = "issue393.subboundary.";
    private static readonly ConcernExpectation[] Concerns =
    [
        new("auth", "Platform / Reference Data", "API host helpers"),
        new("authz", "Platform / Reference Data", "API host helpers"),
        new("http-serialization", "Platform / Reference Data", "API host helpers"),
        new("persisted-serialization", "Platform / Reference Data", "Technical Platform"),
        new("resources-localization", "Platform / Reference Data", "API host helpers"),
        new("logging", "Platform / Reference Data", "API host helpers"),
        new("provider-photo", "Reporting", "Infrastructure Reporting"),
        new("provider-email", "Notifications", "Notifications"),
        new("provider-fcm", "Notifications", "Notifications"),
        new("provider-google", "Identity & Accounts", "Identity"),
        new("provider-elasticsearch", "Platform / Reference Data", "API logging"),
        new("pagination", "Platform / Reference Data", "Technical Platform"),
        new("clock", "Platform / Reference Data", "Technical Platform"),
        new("correlation", "Platform / Reference Data", "Technical Platform"),
        new("api-idempotency", "Platform / Reference Data", "API host helpers"),
        new("background-idempotency", "Platform / Reference Data", "Technical Platform"),
        new("enum-lookup", "Platform / Reference Data", "Reference Data"),
        new("appconfig", "Platform / Reference Data", "Reference Data"),
        new("persistence-topology", "Platform / Reference Data", "Technical Platform")
    ];

    [Test]
    public void Matrix_Should_Reconcile_All_Required_Concerns_With_Production_Sources_And_Locked_Catalogs()
    {
        var rows = ReadRows();
        var concerns = PlatformReferenceDataBoundaryDocumentationTestHelpers.RequireExactIds(
            rows, ConcernPrefix, Concerns.Select(concern => ConcernPrefix + concern.Id));

        AssertConcerns(concerns);
        AssertLockedCatalogs();
        AssertRegistrations();
    }

    [Test]
    public void Matrix_Should_Declare_All_Three_SubBoundaries_NonCanonical()
    {
        var rows = ReadRows();
        var subBoundaries = PlatformReferenceDataBoundaryDocumentationTestHelpers.RequireExactIds(rows, SubBoundaryPrefix,
        [
            "issue393.subboundary.building-blocks",
            "issue393.subboundary.technical-platform",
            "issue393.subboundary.reference-data"
        ]);

        foreach (var row in subBoundaries.Values)
        {
            Assert.That(row.GetField("Canonical module"), Is.EqualTo("Platform / Reference Data"));
            Assert.That(row.GetField("Module/project status"), Is.EqualTo("not a canonical module or project"));
        }
    }

    [Test]
    public void Parser_Should_Reject_An_Omitted_Concern()
    {
        var rows = ReadRows().Where(row => row.Id != "issue393.concern.clock").ToList();

        Assert.That(
            () => PlatformReferenceDataBoundaryDocumentationTestHelpers.RequireExactIds(
                rows, ConcernPrefix, Concerns.Select(concern => ConcernPrefix + concern.Id)),
            Throws.InvalidOperationException.With.Message.Contains("issue393.concern.clock"));
    }

    [Test]
    public void Parser_Should_Reject_Reference_Data_As_A_Ninth_Module()
    {
        var rows = ReadRows();
        var appConfig = rows.Single(row => row.Id == "issue393.concern.appconfig");
        var fixture = rows.Where(row => row != appConfig)
            .Append(appConfig with { Fields = Replace(appConfig.Fields, "Canonical module", "Reference Data") });

        Assert.That(() => AssertConcerns(PlatformReferenceDataBoundaryDocumentationTestHelpers.RequireExactIds(
                fixture, ConcernPrefix, Concerns.Select(concern => ConcernPrefix + concern.Id))),
            Throws.InvalidOperationException.With.Message.Contains("Reference Data is not a canonical module"));
    }

    [Test]
    public void Parser_Should_Reject_A_Provider_Placed_In_BuildingBlocks()
    {
        var rows = ReadRows();
        var fcm = rows.Single(row => row.Id == "issue393.concern.provider-fcm");
        var fixture = rows.Where(row => row != fcm)
            .Append(fcm with { Fields = Replace(fcm.Fields, "Implementation / host", "LgymApi.Platform/BuildingBlocks/FcmPushSender.cs#FcmPushSender") });

        Assert.That(() => AssertConcerns(PlatformReferenceDataBoundaryDocumentationTestHelpers.RequireExactIds(
                fixture, ConcernPrefix, Concerns.Select(concern => ConcernPrefix + concern.Id))),
            Throws.InvalidOperationException.With.Message.Contains("must not be placed in BuildingBlocks"));
    }

    [Test]
    public void Parser_Should_Reject_A_Second_Context_Claim()
    {
        var rows = ReadRows();
        var topology = rows.Single(row => row.Id == "issue393.concern.persistence-topology");
        var fixture = rows.Where(row => row != topology)
            .Append(topology with { Fields = Replace(topology.Fields, "Compatibility invariant", "two AppDbContexts; one database; one migration stream") });

        Assert.That(() => AssertConcerns(PlatformReferenceDataBoundaryDocumentationTestHelpers.RequireExactIds(
                fixture, ConcernPrefix, Concerns.Select(concern => ConcernPrefix + concern.Id))),
            Throws.InvalidOperationException.With.Message.Contains("one AppDbContext"));
    }

    private static IReadOnlyList<BoundaryDocumentationRow> ReadRows()
    {
        var path = Path.Combine(ArchitectureTestHelpers.ResolveRepositoryRoot(), "docs", "modular-monolith", "issue-393-platform-reference-data-boundary.md");
        return PlatformReferenceDataBoundaryDocumentationTestHelpers.ParseRows(File.ReadAllText(path));
    }

    private static void AssertConcerns(IReadOnlyDictionary<string, BoundaryDocumentationRow> rows)
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        foreach (var expectation in Concerns)
        {
            var row = rows[ConcernPrefix + expectation.Id];
            foreach (var field in new[] { "Canonical module", "Owner", "Seam", "Implementation / host", "Allowed dependencies", "Forbidden placement", "Compatibility invariant" })
            {
                Assert.That(row.GetField(field), Is.Not.Empty, $"{row.Id} must define {field}.");
            }

            if (!ArchitectureTestHelpers.GetCanonicalModuleCatalog().Contains(row.GetField("Canonical module"), StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{row.GetField("Canonical module")} is not a canonical module.");
            }

            if (row.GetField("Canonical module") != expectation.CanonicalModule || row.GetField("Owner") != expectation.Owner)
            {
                throw new InvalidOperationException($"{row.Id} has an unexpected canonical module or owner.");
            }

            foreach (var locator in row.GetField("Implementation / host").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (expectation.Id.StartsWith("provider-", StringComparison.Ordinal) && locator.Contains("/BuildingBlocks/", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Provider '{row.Id}' must not be placed in BuildingBlocks.");
                }

                PlatformReferenceDataBoundaryDocumentationTestHelpers.AssertLocatorResolves(root, locator);
            }
        }

        if (rows["issue393.concern.persistence-topology"].GetField("Compatibility invariant") !=
            "one AppDbContext; one PostgreSQL database; one migration stream; logical ownership only")
        {
            throw new InvalidOperationException("Persistence topology must retain one AppDbContext, one database, and one migration stream.");
        }
    }

    private static void AssertLockedCatalogs()
    {
        Assert.That(ArchitectureTestHelpers.GetCanonicalModuleCatalog(), Is.EqualTo(PersistedEntityOwnershipCatalog.CanonicalOwners));
        Assert.That(PersistedEntityOwnershipCatalog.CanonicalOwners, Has.Count.EqualTo(8));
        Assert.That(PersistedEntityOwnershipCatalog.Entries, Has.Count.EqualTo(48));

        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var projects = File.ReadLines(Path.Combine(root, "LgymApi.sln"))
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal)).ToList();
        var edges = projects.Select(line => line.Split('"')[5])
            .Select(path => Path.Combine(root, path))
            .SelectMany(ArchitectureTestHelpers.ParseProjectReferences).ToList();
        Assert.That(projects, Has.Count.EqualTo(18));
        Assert.That(edges, Has.Count.EqualTo(90));
        Assert.That(PersistenceIdentityContract.DbContextSourcePath, Is.EqualTo("LgymApi.Infrastructure/Data/AppDbContext.cs"));
        Assert.That(PersistenceIdentityContract.DbSets, Has.Count.EqualTo(48));
    }

    private static void AssertRegistrations()
    {
        var root = ArchitectureTestHelpers.ResolveRepositoryRoot();
        AssertInvocation(root, "LgymApi.Api/Configuration/ApiAuthenticationExtensions.cs", "AddApiAuthentication", "AddAuthentication");
        AssertInvocation(root, "LgymApi.Api/Configuration/ApiAuthorizationExtensions.cs", "AddApiAuthorizationPolicies", "AddAuthorizationBuilder");
        AssertInvocation(root, "LgymApi.Platform/ReferenceData/ServiceCollectionExtensions.cs", "AddReferenceDataServices", "AddScoped");
        AssertInvocation(root, "LgymApi.Infrastructure/NotificationsServiceCollectionExtensions.cs", "AddNotificationsInfrastructure", "AddScoped");
        AssertInvocation(root, "LgymApi.Notifications/ServiceCollectionExtensions.cs", "AddNotificationsModule", "AddEmailServices");
        AssertInvocation(root, "LgymApi.Platform/PlatformModule.cs", "AddPlatformModule", "AddPlatformPaginationServices");
    }

    private static void AssertInvocation(string root, string relativePath, string method, string invocation)
    {
        Assert.That(PlatformReferenceDataBoundaryDocumentationTestHelpers.MethodInvokes(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)), method, invocation), Is.True);
    }

    private static IReadOnlyDictionary<string, string> Replace(IReadOnlyDictionary<string, string> fields, string key, string value) =>
        fields.ToDictionary(pair => pair.Key, pair => pair.Key == key ? value : pair.Value, StringComparer.Ordinal);

    private sealed record ConcernExpectation(string Id, string CanonicalModule, string Owner);
}
