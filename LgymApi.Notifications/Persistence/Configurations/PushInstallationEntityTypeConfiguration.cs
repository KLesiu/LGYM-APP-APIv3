using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Notifications.Persistence.Configurations;

internal sealed class PushInstallationEntityTypeConfiguration : IEntityTypeConfiguration<PushInstallation>
{
    public void Configure(EntityTypeBuilder<PushInstallation> builder)
    {
        builder.ToTable("PushInstallations");

        builder.Property(e => e.InstallationId).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Platform).HasMaxLength(32).IsRequired();
        builder.Property(e => e.FcmToken).HasMaxLength(512).IsRequired();
        builder.Property(e => e.AppVersion).HasMaxLength(64);
        builder.Property(e => e.Environment).HasMaxLength(32).IsRequired();
        builder.Property(e => e.PermissionStatus).HasMaxLength(64);
        builder.Property(e => e.DisabledReason).HasMaxLength(128);

        builder.HasIndex(e => e.InstallationId)
            .IsUnique()
            .HasFilter(NotificationsConfigurationFilters.ActiveRowsFilter);
        builder.HasIndex(e => e.DisabledAt);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<UserSession>()
            .WithMany()
            .HasForeignKey(e => e.SessionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
