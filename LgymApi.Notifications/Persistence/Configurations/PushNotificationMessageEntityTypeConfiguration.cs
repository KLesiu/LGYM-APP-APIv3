using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Notifications.Persistence.Configurations;

internal sealed class PushNotificationMessageEntityTypeConfiguration : IEntityTypeConfiguration<PushNotificationMessage>
{
    public void Configure(EntityTypeBuilder<PushNotificationMessage> builder)
    {
        builder.ToTable("PushNotificationMessages");

        builder.Property(e => e.Type).HasMaxLength(120).IsRequired();
        builder.Property(e => e.EventId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.EntityId).HasMaxLength(200);
        builder.Property(e => e.Deeplink).HasMaxLength(500);
        builder.Property(e => e.PayloadJson).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>();
        builder.Property(e => e.FailureKind).HasConversion<string>();
        builder.Property(e => e.LastError).HasMaxLength(400);
        builder.Property(e => e.ProviderStatus).HasMaxLength(120);
        builder.Property(e => e.ProviderMessageId).HasMaxLength(200);
        builder.Property(e => e.ProviderErrorCode).HasMaxLength(120);
        builder.Property(e => e.ProviderResponseSummary).HasMaxLength(1000);
        builder.Property(e => e.SchedulerJobId).HasMaxLength(128);

        builder.HasIndex(e => new { e.PushInstallationId, e.Type, e.EventId })
            .IsUnique()
            .HasFilter(NotificationsConfigurationFilters.ActiveRowsFilter);
        builder.HasIndex(e => new { e.Status, e.NextAttemptAt, e.CreatedAt })
            .HasFilter(NotificationsConfigurationFilters.ActiveRowsFilter);
        builder.HasIndex(e => e.CreatedAt);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<PushInstallation>()
            .WithMany()
            .HasForeignKey(e => e.PushInstallationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<InAppNotification>()
            .WithMany()
            .HasForeignKey(e => e.InAppNotificationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
