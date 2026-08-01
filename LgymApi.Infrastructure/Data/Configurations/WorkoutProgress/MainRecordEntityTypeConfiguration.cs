using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.WorkoutProgress;

internal sealed class MainRecordEntityTypeConfiguration : IEntityTypeConfiguration<MainRecord>
{
    public void Configure(EntityTypeBuilder<MainRecord> builder)
    {
        builder.ToTable("MainRecords");

        builder.Ignore(e => e.Weight);
        builder.Property(e => e.WeightValue).HasField("_weightValue").HasColumnName("Weight");
        builder.Property(e => e.Unit).HasField("_unit").HasConversion<string>();
    }
}
