using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NotificationsPersistenceExtractionGuardTests
{
    private static readonly string[] RepositoryFiles =
    [
        "EmailNotificationLogRepository.cs",
        "EmailNotificationSubscriptionRepository.cs",
        "InAppNotificationRepository.cs",
        "PushInstallationRepository.cs",
        "PushNotificationMessageRepository.cs"
    ];

    private static readonly string[] ConfigurationTypes =
    [
        "PushInstallationEntityTypeConfiguration",
        "PushNotificationMessageEntityTypeConfiguration",
        "NotificationMessageEntityTypeConfiguration",
        "EmailNotificationSubscriptionEntityTypeConfiguration",
        "InAppNotificationEntityTypeConfiguration"
    ];

    private static readonly (string Service, string Implementation)[] ScopedRegistrations =
    [
        ("IPushInstallationRepository", "PushInstallationRepository"),
        ("IPushNotificationMessageRepository", "PushNotificationMessageRepository"),
        ("IInAppNotificationRepository", "InAppNotificationRepository"),
        ("IEmailNotificationLogRepository", "EmailNotificationLogRepository"),
        ("IEmailNotificationSubscriptionRepository", "EmailNotificationSubscriptionRepository")
    ];

    [Test]
    public void Notifications_Persistence_Sources_Should_Be_Exclusive_Context_Bounded_And_Stage_Only()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var notificationsRoot = Path.Combine(repositoryRoot, "LgymApi.Notifications");
        var infrastructureRoot = Path.Combine(repositoryRoot, "LgymApi.Infrastructure");
        var sourcePaths = RepositoryFiles
            .Select(file => Path.Combine(notificationsRoot, "Persistence", "Repositories", file))
            .ToArray();

        Assert.That(sourcePaths.All(File.Exists), Is.True);
        Assert.That(
            RepositoryFiles.Select(file => Path.Combine(infrastructureRoot, "Repositories", file)).Where(File.Exists),
            Is.Empty);

        foreach (var sourcePath in sourcePaths)
        {
            var source = File.ReadAllText(sourcePath);

            Assert.That(source, Does.Contain("INotificationsPersistenceContext"), sourcePath);
            EnsureRepositoryDoesNotLeakPersistenceRoot(source);
        }
    }

    [Test]
    public void Notifications_Configurations_Registrar_And_Repository_Registrations_Should_Be_Exact()
    {
        var repositoryRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var notificationsRoot = Path.Combine(repositoryRoot, "LgymApi.Notifications");
        var infrastructureRoot = Path.Combine(repositoryRoot, "LgymApi.Infrastructure");
        var registrar = File.ReadAllText(Path.Combine(notificationsRoot, "Persistence", "NotificationsModelConfigurationRegistrar.cs"));
        var registrations = File.ReadAllText(Path.Combine(notificationsRoot, "ServiceCollectionExtensions.cs"));

        Assert.That(
            ConfigurationTypes.Select(type => Path.Combine(notificationsRoot, "Persistence", "Configurations", type + ".cs")).All(File.Exists),
            Is.True);
        Assert.That(
            ConfigurationTypes.Select(type => Path.Combine(infrastructureRoot, "Data", "Configurations", "Notifications", type + ".cs")).Where(File.Exists),
            Is.Empty);
        EnsureExactConfigurations(ExtractConfigurationTypes(registrar));
        EnsureScopedRegistrations(registrations);
    }

    [Test]
    public void Notifications_Extraction_Guards_Should_Reject_Missing_Duplicate_Save_And_Foreign_Set_Fixtures()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => EnsureExactConfigurations(ConfigurationTypes[..^1]), Throws.InvalidOperationException);
            Assert.That(() => EnsureExactConfigurations([.. ConfigurationTypes, ConfigurationTypes[^1]]), Throws.InvalidOperationException);
            Assert.That(() => EnsureScopedRegistrations("services.AddSingleton<IPushInstallationRepository, PushInstallationRepository>();"), Throws.InvalidOperationException);
            Assert.That(() => EnsureRepositoryDoesNotLeakPersistenceRoot("INotificationsPersistenceContext context; context.SaveChangesAsync();"), Throws.InvalidOperationException.With.Message.Contains("SaveChangesAsync"));
            Assert.That(() => EnsureRepositoryDoesNotLeakPersistenceRoot("INotificationsPersistenceContext context; var users = context.Users;"), Throws.InvalidOperationException.With.Message.Contains("foreign set"));
        });
    }

    private static IReadOnlyList<string> ExtractConfigurationTypes(string source)
    {
        return CSharpSyntaxTree.ParseText(source)
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Select(creation => creation.Type.ToString())
            .Where(type => type.EndsWith("EntityTypeConfiguration", StringComparison.Ordinal))
            .ToArray();
    }

    private static void EnsureExactConfigurations(IReadOnlyList<string> actual)
    {
        if (!actual.SequenceEqual(ConfigurationTypes, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Notifications configuration registrar order changed.");
        }
    }

    private static void EnsureScopedRegistrations(string source)
    {
        var registrations = CSharpSyntaxTree.ParseText(source)
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }
                && genericName.Identifier.ValueText == "AddScoped")
            .Select(invocation => (GenericNameSyntax)((MemberAccessExpressionSyntax)invocation.Expression).Name)
            .Select(name => (Service: name.TypeArgumentList.Arguments[0].ToString(), Implementation: name.TypeArgumentList.Arguments.Count == 2 ? name.TypeArgumentList.Arguments[1].ToString() : null))
            .ToArray();

        foreach (var expected in ScopedRegistrations)
        {
            var matches = registrations.Where(registration => registration.Service == expected.Service).ToArray();
            if (matches.Length != 1 || (matches[0].Implementation is not null && matches[0].Implementation != expected.Implementation))
            {
                throw new InvalidOperationException($"{expected.Service} must be registered once as scoped by NotificationsModule.");
            }
        }
    }

    private static void EnsureRepositoryDoesNotLeakPersistenceRoot(string source)
    {
        foreach (var forbidden in new[] { "AppDbContext", "SaveChangesAsync", "SaveChanges", "BeginTransaction", ".Database" })
        {
            if (source.Contains(forbidden, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Notifications repository leaks persistence root member: {forbidden}.");
            }
        }

        if (source.Contains(".Users", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Notifications repository accesses foreign set: Users.");
        }
    }
}
