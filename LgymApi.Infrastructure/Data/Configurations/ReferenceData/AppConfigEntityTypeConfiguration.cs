using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.ReferenceData;

internal sealed class AppConfigEntityTypeConfiguration : IEntityTypeConfiguration<AppConfig>
{
    public void Configure(EntityTypeBuilder<AppConfig> builder)
    {
        builder.ToTable("AppConfigs");

        builder.Property(e => e.Platform).HasConversion<string>();
    }
}
