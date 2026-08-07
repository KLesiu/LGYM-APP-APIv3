using FluentAssertions;
using LgymApi.Api.Features.InAppNotification;
using LgymApi.Api.Features.InAppNotification.Controllers;
using LgymApi.Api.Features.User.Controllers;
using LgymApi.Api.Hubs;
using LgymApi.Api.Mapping;
using LgymApi.Application.Notifications;
using LgymApi.Application.Repositories;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Notifications.ApiAdapters;
using LgymApi.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class NotificationHostAdapterCompositionTests
{
    [Test]
    public void SignalRPublisher_HostBinding_ResolvesExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        services.AddSingleton<IAccountSessionConnectionRegistry, AccountSessionConnectionRegistry>();
        services.AddSingleton(Substitute.For<IAuthenticatedAccountContextResolver>());
        services.AddScoped<IInAppNotificationPushPublisher, SignalRNotificationPushPublisher>();

        var registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IInAppNotificationPushPublisher))
            .ToArray();
        registrations.Should().ContainSingle()
            .Which.ImplementationType.Should().Be(typeof(SignalRNotificationPushPublisher));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInAppNotificationPushPublisher>()
            .Should().BeOfType<SignalRNotificationPushPublisher>();
    }

    [Test]
    public void NotificationsModule_WhenPublisherHostBindingIsOmitted_FailsWithTheMissingPublisherContract()
    {
        var services = new ServiceCollection();
        services.AddNotificationsModule();
        services.AddScoped(_ => Substitute.For<IInAppNotificationRepository>());
        services.AddScoped(_ => Substitute.For<IUnitOfWork>());
        services.AddScoped(_ => Substitute.For<INotificationEventBridge>());

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var resolve = () => scope.ServiceProvider.GetRequiredService<IInAppNotificationService>();

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(IInAppNotificationPushPublisher)}*");
    }

    [Test]
    public void ApiNotificationAdapters_UsePublicNotificationsContractsAndTheCanonicalMappingMarker()
    {
        var notificationsAssembly = typeof(NotificationReference).Assembly;
        var notificationContracts = new[]
        {
            typeof(IInAppNotificationApiAdapter),
            typeof(INotificationEventApiAdapter),
            typeof(IPushInstallationApiAdapter),
            typeof(IInAppNotificationPushPublisher)
        };

        notificationContracts.Should().OnlyContain(type => type.IsPublic && type.Assembly == notificationsAssembly);
        MappingAssemblyMarkers.All.Should().ContainSingle(assembly => assembly == notificationsAssembly);
        typeof(InAppNotificationController).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).Should().Contain(typeof(IInAppNotificationApiAdapter));
        typeof(PushNotificationAdminController).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).Should().Contain(typeof(INotificationEventApiAdapter));
        typeof(PushInstallationController).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).Should().Contain(typeof(IPushInstallationApiAdapter));
        typeof(SignalRNotificationPushPublisher).GetInterfaces().Should().Contain(typeof(IInAppNotificationPushPublisher));
    }

    [Test]
    public void NotificationsApiAdapters_RegisterAndResolveExactlyOnceAsScoped()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Substitute.For<IInAppNotificationService>());
        services.AddScoped(_ => Substitute.For<INotificationEventBridge>());
        services.AddScoped(_ => Substitute.For<IPushInstallationLifecycleService>());
        services.AddNotificationsApiAdapters();

        AssertScopedRegistration<IInAppNotificationApiAdapter, InAppNotificationApiAdapter>(services);
        AssertScopedRegistration<INotificationEventApiAdapter, NotificationEventApiAdapter>(services);
        AssertScopedRegistration<IPushInstallationApiAdapter, PushInstallationApiAdapter>(services);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IInAppNotificationApiAdapter>()
            .Should().BeOfType<InAppNotificationApiAdapter>();
        scope.ServiceProvider.GetRequiredService<INotificationEventApiAdapter>()
            .Should().BeOfType<NotificationEventApiAdapter>();
        scope.ServiceProvider.GetRequiredService<IPushInstallationApiAdapter>()
            .Should().BeOfType<PushInstallationApiAdapter>();
    }

    private static void AssertScopedRegistration<TContract, TImplementation>(IServiceCollection services)
    {
        services.Where(descriptor => descriptor.ServiceType == typeof(TContract))
            .Should().ContainSingle()
            .Which.Should().Match<ServiceDescriptor>(descriptor =>
                descriptor.Lifetime == ServiceLifetime.Scoped
                && descriptor.ImplementationType == typeof(TImplementation));
    }
}
