using LgymApi.Application.Notifications;
using LgymApi.Application.Options;
using LgymApi.Notifications;
using LgymApi.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IEmailNotificationLeaseSettings>(static provider =>
            new EmailNotificationLeaseSettings(
                provider.GetRequiredService<BackgroundCommandOptions>().EmailSendLeaseSeconds));

        return services;
    }
}
