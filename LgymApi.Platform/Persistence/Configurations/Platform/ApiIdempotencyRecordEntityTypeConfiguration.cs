using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Platform;

internal sealed class ApiIdempotencyRecordEntityTypeConfiguration : IEntityTypeConfiguration<ApiIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<ApiIdempotencyRecord> builder)
    {
        builder.ToTable("ApiIdempotencyRecords");

        builder.Property(e => e.IdempotencyKey).IsRequired().HasMaxLength(256);
        builder.Property(e => e.ScopeTuple).IsRequired().HasMaxLength(512);
        builder.Property(e => e.RequestFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(e => e.ResponseBodyJson).IsRequired();

        builder.HasIndex(e => new { e.ScopeTuple, e.IdempotencyKey })
            .IsUnique()
            .HasFilter(PlatformConfigurationFilters.ActiveRowsFilter);
        builder.HasIndex(e => new { e.ScopeTuple, e.IdempotencyKey, e.RequestFingerprint })
            .HasFilter(PlatformConfigurationFilters.ActiveRowsFilter);
    }
}
