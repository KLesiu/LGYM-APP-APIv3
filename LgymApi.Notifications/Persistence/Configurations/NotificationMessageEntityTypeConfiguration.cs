using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Notifications.Persistence.Configurations;

internal sealed class NotificationMessageEntityTypeConfiguration : IEntityTypeConfiguration<NotificationMessage>
{
    public void Configure(EntityTypeBuilder<NotificationMessage> builder)
    {
        builder.ToTable("NotificationMessages");

        builder.Property(e => e.Channel).HasConversion<string>();
        builder.Property(e => e.Status).HasConversion<string>();
        builder.Property(e => e.Type)
            .HasConversion(
                notificationType => notificationType.Value,
                value => EmailNotificationType.Parse(value))
            .IsRequired();
        builder.Property(e => e.Recipient)
            .HasConversion(email => email.Value, value => new Email(value));
        builder.Property(e => e.Recipient).IsRequired();
        builder.Property(e => e.PayloadJson).IsRequired();

        builder.HasIndex(e => new { e.Status, e.NextAttemptAt, e.CreatedAt });
        builder.HasIndex(e => new { e.Channel, e.Type, e.CorrelationId, e.Recipient })
            .IsUnique()
            .HasFilter(NotificationsConfigurationFilters.ActiveRowsFilter);
    }
}
