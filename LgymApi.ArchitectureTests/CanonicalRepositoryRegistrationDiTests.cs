using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Repositories;
using LgymApi.Infrastructure;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Notifications.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class CanonicalRepositoryRegistrationDiTests
{
    [Test]
    public void AddInfrastructure_Should_Register_Canonical_Repositories_Once_AsScoped_And_Resolve_Them()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["Email:Enabled"] = "true",
                ["Email:InvitationBaseUrl"] = "https://example.com/invite",
                ["Email:PasswordRecoveryBaseUrl"] = "https://example.com/reset",
                ["Email:TemplateRootPath"] = "EmailTemplates",
                ["Email:DefaultCulture"] = "en-US",
                ["Email:FromAddress"] = "coach@example.com",
                ["Email:SmtpHost"] = "smtp.example.com",
                ["Email:SmtpPort"] = "587"
            })
            .Build();

        services.AddNotificationsModule();
        services.AddInfrastructure(configuration, enableSensitiveLogging: false, isTesting: true);

        var expectedRegistrations = new[]
        {
            (Module: "WorkoutProgress", ServiceTypeName: typeof(IEloRegistryRepository).FullName!, ImplementationName: nameof(EloRegistryRepository)),
            (Module: "WorkoutProgress", ServiceTypeName: typeof(IMainRecordRepository).FullName!, ImplementationName: nameof(MainRecordRepository)),
            (Module: "Notifications", ServiceTypeName: "LgymApi.Application.Notifications.Repositories.IPushInstallationRepository", ImplementationName: "PushInstallationRepository"),
            (Module: "Notifications", ServiceTypeName: "LgymApi.Application.Repositories.IPushNotificationMessageRepository", ImplementationName: "PushNotificationMessageRepository"),
            (Module: "Notifications", ServiceTypeName: "LgymApi.Application.Notifications.IInAppNotificationRepository", ImplementationName: "InAppNotificationRepository"),
            (Module: "Notifications", ServiceTypeName: "LgymApi.Application.Repositories.IEmailNotificationLogRepository", ImplementationName: "EmailNotificationLogRepository"),
            (Module: "Notifications", ServiceTypeName: "LgymApi.Application.Repositories.IEmailNotificationSubscriptionRepository", ImplementationName: "EmailNotificationSubscriptionRepository")
        };

        var emailLogDescriptor = services.Single(descriptor => descriptor.ServiceType.FullName == "LgymApi.Application.Repositories.IEmailNotificationLogRepository");
        Assert.That(emailLogDescriptor.ImplementationFactory, Is.Not.Null, "Email notification log repository must retain its factory registration.");

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        foreach (var expected in expectedRegistrations)
        {
            var descriptors = services.Where(descriptor => descriptor.ServiceType.FullName == expected.ServiceTypeName).ToList();

            Assert.That(descriptors, Has.Count.EqualTo(1), $"Expected one registration for {expected.ServiceTypeName}.");
            Assert.That(descriptors.Single().Lifetime, Is.EqualTo(ServiceLifetime.Scoped), $"Expected scoped lifetime for {expected.ServiceTypeName}.");

            var resolved = scope.ServiceProvider.GetRequiredService(descriptors.Single().ServiceType);
            Assert.That(resolved.GetType().Name, Is.EqualTo(expected.ImplementationName), $"Unexpected implementation for {expected.ServiceTypeName}.");
            if (expected.Module == "Notifications")
            {
                Assert.That(resolved.GetType().Assembly, Is.EqualTo(typeof(NotificationReference).Assembly));
            }

            TestContext.Progress.WriteLine(
                $"module={expected.Module}; service={expected.ServiceTypeName}; lifetime={descriptors.Single().Lifetime}; implementation={resolved.GetType().Name}");
        }
    }

    [TestCase("/LgymApi.Application/Repositories/IEloRegistryRepository.cs", "Workout & Progress")]
    [TestCase("/LgymApi.Application/Repositories/IMainRecordRepository.cs", "Workout & Progress")]
    [TestCase("/LgymApi.Notifications/Repositories/IEmailNotificationLogRepository.cs", "Notifications")]
    [TestCase("/LgymApi.Notifications/Repositories/IEmailNotificationSubscriptionRepository.cs", "Notifications")]
    [TestCase("/LgymApi.Application/EloRegistry/EloRegistryService.cs", "Workout & Progress")]
    [TestCase("/LgymApi.Application/MainRecords/MainRecordsService.cs", "Workout & Progress")]
    [TestCase("/LgymApi.Infrastructure/Repositories/EloRegistryRepository.cs", "Workout & Progress")]
    [TestCase("/LgymApi.Infrastructure/Repositories/MainRecordRepository.cs", "Workout & Progress")]
    [TestCase("/LgymApi.Notifications/Persistence/Repositories/EmailNotificationLogRepository.cs", "Notifications")]
    [TestCase("/LgymApi.Notifications/Persistence/Repositories/EmailNotificationSubscriptionRepository.cs", "Notifications")]
    [TestCase("/LgymApi.Notifications/Persistence/Repositories/InAppNotificationRepository.cs", "Notifications")]
    [TestCase("/LgymApi.Notifications/Persistence/Repositories/PushInstallationRepository.cs", "Notifications")]
    [TestCase("/LgymApi.Notifications/Persistence/Repositories/PushNotificationMessageRepository.cs", "Notifications")]
    public void ArchitectureTestHelpers_Should_Resolve_Canonical_Repository_Owners(string path, string expectedModule)
    {
        Assert.That(ArchitectureTestHelpers.GetCanonicalModuleNameFromPath(path), Is.EqualTo(expectedModule));
    }
}
