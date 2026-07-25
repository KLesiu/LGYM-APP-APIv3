using FluentAssertions;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Infrastructure;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Services;
using LgymApi.TestUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class NotificationsEmailServiceCollectionExtensionsTests
{
    private static readonly Type[] ExpectedComposerTypes =
    [
        typeof(TrainerInvitationEmailTemplateComposer),
        typeof(TrainerInvitationAcceptedEmailTemplateComposer),
        typeof(TrainerInvitationRevokedEmailTemplateComposer),
        typeof(TrainingCompletedEmailTemplateComposer),
        typeof(WelcomeEmailTemplateComposer),
        typeof(PasswordRecoveryEmailTemplateComposer)
    ];

    [Test]
    public void AddNotificationsInfrastructure_OwnsEveryEmailDescriptorExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = CreateConfiguration();

        services.AddPlatformServices(configuration, enableSensitiveLogging: false, isTesting: true);

        AssertEmailDescriptorsAreAbsent(services);

        services.AddNotificationsInfrastructure(configuration);

        AssertEmailDescriptors(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEmailSender>().Should().BeOfType<SmtpEmailSender>();
        scope.ServiceProvider.GetServices<IEmailTemplateComposer>().Select(composer => composer.GetType())
            .Should().BeEquivalentTo(ExpectedComposerTypes);
    }

    [Test]
    public void EmailDescriptorValidation_RejectsMissingNotificationRegistration()
    {
        var services = CreateComposedServices();
        services.Remove(GetSingleDescriptor(services, typeof(EmailOptions)));

        var action = () => AssertEmailDescriptors(services);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(EmailOptions).FullName}*exactly once*");
    }

    [Test]
    public void EmailDescriptorValidation_RejectsDuplicateNotificationRegistration()
    {
        var services = CreateComposedServices();
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        var action = () => AssertEmailDescriptors(services);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(IEmailSender).FullName}*exactly once*");
    }

    private static ServiceCollection CreateComposedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = CreateConfiguration();
        services.AddPlatformServices(configuration, enableSensitiveLogging: false, isTesting: true);
        services.AddNotificationsInfrastructure(configuration);
        return services;
    }

    private static IConfiguration CreateConfiguration() =>
        TestConfigurationBuilder.BuildEnabledEmailConfiguration();

    private static void AssertEmailDescriptorsAreAbsent(IServiceCollection services)
    {
        foreach (var serviceType in GetEmailServiceTypes())
        {
            services.Should().NotContain(descriptor => descriptor.ServiceType == serviceType);
        }
    }

    private static void AssertEmailDescriptors(IServiceCollection services)
    {
        AssertInstanceDescriptor(services, typeof(EmailOptions), ServiceLifetime.Singleton);
        AssertTypeDescriptor(services, typeof(IEmailNotificationsFeature), typeof(EmailNotificationsFeature), ServiceLifetime.Singleton);
        AssertTypeDescriptor(services, typeof(IEmailMetrics), typeof(EmailMetrics), ServiceLifetime.Singleton);
        AssertTypeDescriptor(services, typeof(IEmailTemplateComposerFactory), typeof(EmailTemplateComposerFactory), ServiceLifetime.Scoped);
        AssertTypeDescriptor(services, typeof(SmtpEmailSender), typeof(SmtpEmailSender), ServiceLifetime.Scoped);
        AssertTypeDescriptor(services, typeof(DummyEmailSender), typeof(DummyEmailSender), ServiceLifetime.Scoped);
        AssertFactoryDescriptor(services, typeof(IEmailSender), ServiceLifetime.Scoped);

        var composerDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IEmailTemplateComposer))
            .ToArray();
        if (composerDescriptors.Length != ExpectedComposerTypes.Length
            || composerDescriptors.Any(descriptor => descriptor.Lifetime != ServiceLifetime.Scoped
                || descriptor.ImplementationFactory is not null
                || descriptor.ImplementationInstance is not null)
            || !composerDescriptors.Select(descriptor => descriptor.ImplementationType).ToHashSet().SetEquals(ExpectedComposerTypes))
        {
            throw new InvalidOperationException("Email template composers must use the exact six scoped type descriptors.");
        }
    }

    private static IEnumerable<Type> GetEmailServiceTypes()
    {
        yield return typeof(EmailOptions);
        yield return typeof(IEmailNotificationsFeature);
        yield return typeof(IEmailMetrics);
        yield return typeof(IEmailTemplateComposer);
        yield return typeof(IEmailTemplateComposerFactory);
        yield return typeof(SmtpEmailSender);
        yield return typeof(DummyEmailSender);
        yield return typeof(IEmailSender);
    }

    private static void AssertTypeDescriptor(
        IServiceCollection services,
        Type serviceType,
        Type implementationType,
        ServiceLifetime lifetime)
    {
        var descriptor = GetSingleDescriptor(services, serviceType);
        if (descriptor.Lifetime != lifetime
            || descriptor.ImplementationType != implementationType
            || descriptor.ImplementationFactory is not null
            || descriptor.ImplementationInstance is not null)
        {
            throw new InvalidOperationException(
                $"Service '{serviceType.FullName}' must use one {lifetime} type descriptor for '{implementationType.FullName}'.");
        }
    }

    private static void AssertFactoryDescriptor(
        IServiceCollection services,
        Type serviceType,
        ServiceLifetime lifetime)
    {
        var descriptor = GetSingleDescriptor(services, serviceType);
        if (descriptor.Lifetime != lifetime
            || descriptor.ImplementationFactory is null
            || descriptor.ImplementationType is not null
            || descriptor.ImplementationInstance is not null)
        {
            throw new InvalidOperationException($"Service '{serviceType.FullName}' must use one {lifetime} factory descriptor.");
        }
    }

    private static void AssertInstanceDescriptor(
        IServiceCollection services,
        Type serviceType,
        ServiceLifetime lifetime)
    {
        var descriptor = GetSingleDescriptor(services, serviceType);
        if (descriptor.Lifetime != lifetime
            || descriptor.ImplementationInstance is null
            || descriptor.ImplementationType is not null
            || descriptor.ImplementationFactory is not null)
        {
            throw new InvalidOperationException($"Service '{serviceType.FullName}' must use one {lifetime} instance descriptor.");
        }
    }

    private static ServiceDescriptor GetSingleDescriptor(IServiceCollection services, Type serviceType)
    {
        var descriptors = services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();
        if (descriptors.Length != 1)
        {
            throw new InvalidOperationException(
                $"Service '{serviceType.FullName}' must be registered exactly once; actual count is {descriptors.Length}.");
        }

        return descriptors[0];
    }
}
