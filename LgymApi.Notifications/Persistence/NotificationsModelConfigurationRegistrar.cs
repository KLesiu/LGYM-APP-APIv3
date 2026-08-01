using LgymApi.Notifications.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Notifications.Persistence;

internal static class NotificationsModelConfigurationRegistrar
{
    internal static void Apply(ModelBuilder modelBuilder)
    {
        Register(modelBuilder, new PushInstallationEntityTypeConfiguration());
        Register(modelBuilder, new PushNotificationMessageEntityTypeConfiguration());
        Register(modelBuilder, new NotificationMessageEntityTypeConfiguration());
        Register(modelBuilder, new EmailNotificationSubscriptionEntityTypeConfiguration());
        Register(modelBuilder, new InAppNotificationEntityTypeConfiguration());
    }

    private static void Register<TEntity>(ModelBuilder modelBuilder, IEntityTypeConfiguration<TEntity> configuration)
        where TEntity : class
    {
        modelBuilder.ApplyConfiguration(configuration);
    }
}
