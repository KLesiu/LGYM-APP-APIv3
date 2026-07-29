using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Notifications.Persistence.Configurations;

internal sealed class EmailNotificationSubscriptionEntityTypeConfiguration : IEntityTypeConfiguration<EmailNotificationSubscription>
{
    public void Configure(EntityTypeBuilder<EmailNotificationSubscription> builder)
    {
        builder.ToTable("EmailNotificationSubscriptions");

        builder.Property(e => e.NotificationType).IsRequired();

        builder.HasIndex(e => new { e.UserId, e.NotificationType })
            .IsUnique()
            .HasFilter(NotificationsConfigurationFilters.ActiveRowsFilter);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
