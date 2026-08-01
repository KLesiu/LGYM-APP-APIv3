using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Notifications.Persistence.Configurations;

internal sealed class InAppNotificationEntityTypeConfiguration : IEntityTypeConfiguration<InAppNotification>
{
    public void Configure(EntityTypeBuilder<InAppNotification> builder)
    {
        builder.ToTable("in_app_notifications");

        builder.Property(e => e.DeliveryKey)
            .HasMaxLength(200);
        builder.Property(e => e.Type)
            .HasConversion(
                notificationType => notificationType.Value,
                value => InAppNotificationType.Parse(value))
            .IsRequired();

        builder.HasIndex(e => new { e.RecipientId, e.CreatedAt, e.Id });
        builder.HasIndex(e => new { e.RecipientId, e.Type, e.DeliveryKey })
            .IsUnique()
            .HasFilter(NotificationsConfigurationFilters.ActiveDeliveryKeyFilter);
    }
}
