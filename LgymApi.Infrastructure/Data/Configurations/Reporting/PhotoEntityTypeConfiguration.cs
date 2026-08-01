using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Reporting;

internal sealed class PhotoEntityTypeConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.ToTable("Photos");

        builder.Property(e => e.StorageKey).IsRequired();
        builder.Property(e => e.MimeType).IsRequired();
        builder.Property(e => e.SizeBytes).IsRequired();
        builder.Property(e => e.Checksum).IsRequired();

        builder.HasIndex(e => new { e.ReportRequestId, e.ViewType })
            .IsUnique()
            .HasFilter(ReportingConfigurationFilters.ActiveRowsFilter);
        builder.HasIndex(e => new { e.OwnerUserId, e.CreatedAt });

        builder.HasOne(e => e.ReportRequest)
            .WithMany()
            .HasForeignKey(e => e.ReportRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Uploader)
            .WithMany()
            .HasForeignKey(e => e.UploaderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Owner)
            .WithMany()
            .HasForeignKey(e => e.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
