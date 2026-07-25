using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Infrastructure.Configuration;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Notifications.Push;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddNotificationsModule();
        services.AddNotificationsInfrastructure(configuration);

        return services;
    }

    public static IServiceCollection AddNotificationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var appDefaultsOptions = AppDefaultsOptionsFactory.Resolve(configuration);

        AddEmailInfrastructure(services, configuration, appDefaultsOptions);

        var pushNotificationOptions = PushNotificationOptionsFactory.Create(configuration);

        PushNotificationOptionsFactory.Validate(pushNotificationOptions);

        services.AddSingleton(pushNotificationOptions);
        services.AddSingleton<IStalePushInstallationCleanupSettings, PushInstallationCleanupSettings>();
        services.AddSingleton<IPushNotificationDeliveryRetrySettings, PushNotificationDeliveryRetrySettings>();
        services.AddScoped<IPushProviderSender, FcmPushSender>();

        services.AddScoped<IPushInstallationRepository, PushInstallationRepository>();
        services.AddScoped<IPushNotificationMessageRepository, PushNotificationMessageRepository>();
        services.AddScoped<IInAppNotificationRepository, InAppNotificationRepository>();
        services.AddScoped<IEmailNotificationLogRepository>(sp =>
            new EmailNotificationLogRepository(
                sp.GetRequiredService<AppDbContext>(),
                sp.GetRequiredService<BackgroundCommandOptions>()));
        services.AddScoped<IEmailNotificationSubscriptionRepository, EmailNotificationSubscriptionRepository>();

        return services;
    }
}
