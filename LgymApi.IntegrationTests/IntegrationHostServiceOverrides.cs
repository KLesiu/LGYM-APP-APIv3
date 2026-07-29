using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Infrastructure.Data;
using LgymApi.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LgymApi.IntegrationTests;

internal static class IntegrationHostServiceOverrides
{
    public static void RemoveAppDbContextRegistrations(IServiceCollection services)
    {
        var descriptorsToRemove = services.Where(IsAppDbContextRegistration).ToList();
        foreach (var descriptor in descriptorsToRemove)
        {
            services.Remove(descriptor);
        }
    }

    public static void ReplaceExternalEffects(
        IServiceCollection services,
        TestEmailSender emailSender,
        TestPushProviderSender pushSender)
    {
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender>(emailSender);
        services.RemoveAll<IPushProviderSender>();
        services.AddSingleton<IPushProviderSender>(pushSender);
    }

    private static bool IsAppDbContextRegistration(ServiceDescriptor descriptor)
    {
        var serviceType = descriptor.ServiceType;
        return serviceType == typeof(AppDbContext)
            || serviceType == typeof(DbContextOptions)
            || serviceType == typeof(DbContextOptions<AppDbContext>)
            || serviceType == typeof(IDbContextOptionsConfiguration<AppDbContext>);
    }
}
