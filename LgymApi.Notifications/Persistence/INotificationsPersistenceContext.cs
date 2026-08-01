using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace LgymApi.Notifications.Persistence;

internal interface INotificationsPersistenceContext
{
    DbSet<NotificationMessage> NotificationMessages { get; }
    DbSet<EmailNotificationSubscription> EmailNotificationSubscriptions { get; }
    DbSet<PushInstallation> PushInstallations { get; }
    DbSet<PushNotificationMessage> PushNotificationMessages { get; }
    DbSet<InAppNotification> InAppNotifications { get; }

    EntityEntry<InAppNotification> Entry(InAppNotification entity);
    EntityEntry<PushNotificationMessage> Entry(PushNotificationMessage entity);
}
