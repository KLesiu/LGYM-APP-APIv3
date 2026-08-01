using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Nutrition;

internal sealed class DietPlanHistoryEntityTypeConfiguration : IEntityTypeConfiguration<DietPlanHistory>
{
    public void Configure(EntityTypeBuilder<DietPlanHistory> builder)
    {
        builder.ToTable("DietPlanHistories");

        builder.Property(e => e.ChangeType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.SnapshotJson).HasColumnType("text").IsRequired();

        builder.HasIndex(e => new { e.DietPlanId, e.ChangeDate });

        builder.HasOne(e => e.DietPlan)
            .WithMany(e => e.HistoryEntries)
            .HasForeignKey(e => e.DietPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.ChangedByUser)
            .WithMany()
            .HasForeignKey(e => e.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
