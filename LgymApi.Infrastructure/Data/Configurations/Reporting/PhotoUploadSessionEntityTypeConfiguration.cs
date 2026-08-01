using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Reporting;

internal sealed class PhotoUploadSessionEntityTypeConfiguration : IEntityTypeConfiguration<PhotoUploadSession>
{
    public void Configure(EntityTypeBuilder<PhotoUploadSession> builder)
    {
        builder.ToTable("PhotoUploadSessions");

        builder.Property(e => e.StorageKey).IsRequired();
        builder.Property(e => e.DeclaredContentType).HasMaxLength(128).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>();
        builder.Property(e => e.FailureReason).HasMaxLength(500);

        builder.HasIndex(e => e.StorageKey)
            .IsUnique()
            .HasFilter(ReportingConfigurationFilters.ActiveRowsFilter);
        builder.HasIndex(e => new { e.OwnerUserId, e.CreatedAt });
        builder.HasIndex(e => new { e.ReportRequestId, e.ViewType });
        builder.HasIndex(e => new { e.Status, e.ExpiresAtUtc });

        builder.HasOne(e => e.ReportRequest)
            .WithMany()
            .HasForeignKey(e => e.ReportRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.OwnerUser)
            .WithMany()
            .HasForeignKey(e => e.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.InitiatedByUser)
            .WithMany()
            .HasForeignKey(e => e.InitiatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CompletedPhoto)
            .WithMany()
            .HasForeignKey(e => e.CompletedPhotoId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
