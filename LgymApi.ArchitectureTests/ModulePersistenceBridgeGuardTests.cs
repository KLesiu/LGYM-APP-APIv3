using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure;
using LgymApi.Infrastructure.Data;
using LgymApi.Notifications.Contracts;
using LgymApi.Platform.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class ModulePersistenceBridgeGuardTests
{
    private static readonly string[] ApprovedFriends =
    [
        "LgymApi.Infrastructure",
        "LgymApi.UnitTests",
        "LgymApi.IntegrationTests"
    ];

    [Test]
    public void Module_Contexts_Should_Be_Internal_Exact_And_Resolve_To_The_Scoped_AppDbContext()
    {
        var contexts = new[]
        {
            Context(typeof(ActorReference).Assembly, "LgymApi.Platform.Persistence.IPlatformPersistenceContext", ["AppConfigs", "ActionExecutionLogs", "CommandEnvelopes", "ApiIdempotencyRecords"], ["CommandEnvelope"]),
            Context(typeof(AccountReference).Assembly, "LgymApi.Identity.Persistence.IIdentityPersistenceContext", ["Users", "Roles", "UserRoles", "RoleClaims", "PasswordResetTokens", "UserExternalLogins", "UserSessions", "UserTutorialProgresses", "UserTutorialStepProgresses", "ProviderName"], []),
            Context(typeof(PlanReference).Assembly, "LgymApi.TrainingPlanning.Persistence.ITrainingPlanningPersistenceContext", ["Plans", "PlanDays", "PlanDayExercises"], []),
            Context(typeof(NotificationReference).Assembly, "LgymApi.Notifications.Persistence.INotificationsPersistenceContext", ["NotificationMessages", "EmailNotificationSubscriptions", "PushInstallations", "PushNotificationMessages", "InAppNotifications"], ["InAppNotification", "PushNotificationMessage"])
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformServices(CreateConfiguration(), enableSensitiveLogging: false, isTesting: true);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var appDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var context in contexts)
        {
            AssertExactContextSurface(context);
            Assert.That(ReferenceEquals(appDbContext, scope.ServiceProvider.GetRequiredService(context.Type)), Is.True, context.Type.FullName);
        }

        var appDbContextSource = File.ReadAllText(Path.Combine(ArchitectureTestHelpers.ResolveRepositoryRoot(), "LgymApi.Infrastructure", "Data", "AppDbContext.cs"));
        foreach (var context in contexts)
        {
            Assert.That(appDbContextSource, Does.Contain($"{context.Type.Name}."));
        }
    }

    [Test]
    public void Module_Model_Registrars_Should_Preserve_The_Explicit_Global_Phase_Order()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var registrar = File.ReadAllText(Path.Combine(repoRoot, "LgymApi.Infrastructure", "Data", "Configurations", "AppDbContextEntityTypeConfigurationRegistrar.cs"));

        Assert.That(registrar, Does.Not.Contain("ApplyConfigurationsFromAssembly"));
        Assert.That(
            () => EnsureExactRegistrarPhases(
                ExtractInvocationNames(registrar),
                PersistenceIdentityContract.RegistrarPhases),
            Throws.Nothing);
    }

    [Test]
    public void Module_Model_Registrar_Phase_Fixtures_Should_Reject_Missing_And_Duplicate_Phases()
    {
        var missing = PersistenceIdentityContract.RegistrarPhases
            .Where(phase => phase != "NotificationsModelConfigurationRegistrar.Apply")
            .ToArray();
        var duplicate = PersistenceIdentityContract.RegistrarPhases
            .Append("NotificationsModelConfigurationRegistrar.Apply")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                () => EnsureExactRegistrarPhases(
                    ExtractInvocationNames(RegistrarPhaseFixture(missing)),
                    PersistenceIdentityContract.RegistrarPhases),
                Throws.InvalidOperationException.With.Message.Contains("NotificationsModelConfigurationRegistrar.Apply"));
            Assert.That(
                () => EnsureExactRegistrarPhases(
                    ExtractInvocationNames(RegistrarPhaseFixture(duplicate)),
                    PersistenceIdentityContract.RegistrarPhases),
                Throws.InvalidOperationException.With.Message.Contains("NotificationsModelConfigurationRegistrar.Apply"));
        });
    }

    [Test]
    public void Module_InternalsVisibleTo_Lists_Should_Be_Exact()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var modulePaths = new[] { "LgymApi.Platform", "LgymApi.Identity", "LgymApi.TrainingPlanning", "LgymApi.Notifications" };

        foreach (var modulePath in modulePaths)
        {
            var assemblyInfo = Path.Combine(repoRoot, modulePath, "Properties", "AssemblyInfo.cs");
            Assert.That(ReadFriends(File.ReadAllText(assemblyInfo)), Is.EqualTo(ApprovedFriends), modulePath);
        }

        Assert.That(
            () => EnsureExactFriends([.. ApprovedFriends, "LgymApi.Api"]),
            Throws.InvalidOperationException.With.Message.Contains("LgymApi.Api"));
        Assert.That(
            () => EnsureExactFriends([.. ApprovedFriends, "LgymApi.DataSeeder"]),
            Throws.InvalidOperationException.With.Message.Contains("LgymApi.DataSeeder"));
    }

    [Test]
    public void Persistence_Bridge_Fixtures_Should_Reject_Missing_Duplicate_Foreign_And_Save_Leaks()
    {
        var duplicateRegistrar = PersistenceTopologyGuardTestHelpers.Analyze(
        [
            new TopologySource("LgymApi.Infrastructure/Data/Configurations/AppDbContextEntityTypeConfigurationRegistrar.cs", RegistrarFixture("Register(modelBuilder, new UserConfiguration()); Register(modelBuilder, new UserConfiguration());"))
        ]);
        var missingRegistrar = PersistenceTopologyGuardTestHelpers.Analyze(
        [
            new TopologySource("LgymApi.Infrastructure/Data/Configurations/AppDbContextEntityTypeConfigurationRegistrar.cs", RegistrarFixture(string.Empty))
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => PersistenceTopologyGuardTestHelpers.EnsureRegistrarOrder(duplicateRegistrar, ["UserConfiguration"], "LgymApi.Infrastructure/Data/Configurations/AppDbContextEntityTypeConfigurationRegistrar.cs"),
                Throws.InvalidOperationException);
            Assert.That(
                () => PersistenceTopologyGuardTestHelpers.EnsureRegistrarOrder(missingRegistrar, ["UserConfiguration"], "LgymApi.Infrastructure/Data/Configurations/AppDbContextEntityTypeConfigurationRegistrar.cs"),
                Throws.InvalidOperationException);
            Assert.That(
                () => EnsureExactPropertyNames(ExtractPropertyNames("internal interface ITrainingPlanningPersistenceContext { DbSet<Plan> Plans { get; } DbSet<User> Users { get; } }"), ["Plans"]),
                Throws.InvalidOperationException.With.Message.Contains("Users"));
            Assert.That(
                () => EnsureNoForbiddenPersistenceMembers("internal interface IIdentityPersistenceContext { Task<int> SaveChangesAsync() => Task.FromResult(0); }"),
                Throws.InvalidOperationException.With.Message.Contains("SaveChangesAsync"));
        });
    }

    private static PersistenceContextExpectation Context(Assembly assembly, string typeName, string[] properties, string[] entryEntities)
    {
        return new PersistenceContextExpectation(assembly.GetType(typeName, throwOnError: true)!, properties, entryEntities);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=persistence-bridge;Username=test;Password=test"
            })
            .Build();
    }

    private static void AssertExactContextSurface(PersistenceContextExpectation expectation)
    {
        var properties = expectation.Type.GetProperties();
        var actualProperties = properties.Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var expectedProperties = expectation.Properties.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var entryMethods = expectation.Type.GetMethods().Where(method => method.Name == "Entry").ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(expectation.Type.IsInterface && !expectation.Type.IsPublic, Is.True, expectation.Type.FullName);
            Assert.That(actualProperties, Is.EqualTo(expectedProperties));
            Assert.That(properties.Where(property => property.Name != "ProviderName").All(IsOwnedDbSet), Is.True);
            Assert.That(properties.SingleOrDefault(property => property.Name == "ProviderName")?.PropertyType, Is.EqualTo(expectation.Properties.Contains("ProviderName") ? typeof(string) : null));
            Assert.That(entryMethods.All(method => !method.IsGenericMethod), Is.True);
            Assert.That(entryMethods.Select(method => method.GetParameters().Single().ParameterType.Name), Is.EquivalentTo(expectation.EntryEntities));
            Assert.That(entryMethods.All(method => method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(EntityEntry<>)), Is.True);
            Assert.That(expectation.Type.GetMembers().Select(member => member.Name), Does.Not.Contain("SaveChangesAsync"));
            Assert.That(expectation.Type.GetMembers().Select(member => member.Name), Does.Not.Contain("Database"));
            Assert.That(expectation.Type.GetMembers().Select(member => member.Name), Does.Not.Contain("BeginTransactionAsync"));
        });
    }

    private static bool IsOwnedDbSet(System.Reflection.PropertyInfo property)
    {
        return property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>);
    }

    private static IReadOnlyList<string> ExtractInvocationNames(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var apply = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Apply");
        return apply.Body!.Statements.Select(statement => statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Single()).Select(invocation => invocation.Expression.ToString()).ToList();
    }

    private static void EnsureExactRegistrarPhases(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        if (actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            return;
        }

        var missing = expected.Except(actual, StringComparer.Ordinal);
        var duplicates = actual
            .GroupBy(phase => phase, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);
        var unexpected = actual.Except(expected, StringComparer.Ordinal);
        throw new InvalidOperationException(
            $"Registrar phases changed. Missing: {string.Join(", ", missing)}. " +
            $"Duplicate: {string.Join(", ", duplicates)}. Unexpected: {string.Join(", ", unexpected)}.");
    }

    private static IReadOnlyList<string> ReadFriends(string source)
    {
        return CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot().DescendantNodes().OfType<AttributeSyntax>()
            .Where(attribute => attribute.Name.ToString().EndsWith("InternalsVisibleTo", StringComparison.Ordinal))
            .Select(attribute => attribute.ArgumentList!.Arguments.Single().Expression.ToString().Trim('"'))
            .ToList();
    }

    private static void EnsureExactFriends(IReadOnlyList<string> friends)
    {
        if (!friends.SequenceEqual(ApprovedFriends, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected InternalsVisibleTo assembly: {string.Join(", ", friends)}.");
        }
    }

    private static IReadOnlyList<string> ExtractPropertyNames(string source)
    {
        return CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Select(property => property.Identifier.ValueText)
            .ToList();
    }

    private static void EnsureExactPropertyNames(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected persistence set: {string.Join(", ", actual.Except(expected, StringComparer.Ordinal))}.");
        }
    }

    private static void EnsureNoForbiddenPersistenceMembers(string source)
    {
        var members = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Select(method => method.Identifier.ValueText);
        var forbidden = members.SingleOrDefault(member => member is "SaveChanges" or "SaveChangesAsync" or "BeginTransaction" or "BeginTransactionAsync");
        if (forbidden is not null)
        {
            throw new InvalidOperationException($"Forbidden module persistence member: {forbidden}.");
        }
    }

    private static string RegistrarFixture(string registrations)
    {
        return $"using Microsoft.EntityFrameworkCore; using Microsoft.EntityFrameworkCore.Metadata.Builders; class User {{ }} sealed class UserConfiguration : IEntityTypeConfiguration<User> {{ public void Configure(EntityTypeBuilder<User> builder) {{ }} }} static class AppDbContextEntityTypeConfigurationRegistrar {{ static void Register<T>(ModelBuilder modelBuilder, IEntityTypeConfiguration<T> configuration) where T : class {{ }} static void Apply(ModelBuilder modelBuilder) {{ {registrations} }} }}";
    }

    private static string RegistrarPhaseFixture(IEnumerable<string> phases)
    {
        var invocations = string.Join(' ', phases.Select(phase => $"{phase}(modelBuilder);"));
        return $"static class AppDbContextEntityTypeConfigurationRegistrar {{ static void Apply(object modelBuilder) {{ {invocations} }} }}";
    }

    private sealed record PersistenceContextExpectation(Type Type, string[] Properties, string[] EntryEntities);
}
