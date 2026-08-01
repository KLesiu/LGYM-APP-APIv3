using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Nutrition;

internal sealed class SupplementPlanItemEntityTypeConfiguration : IEntityTypeConfiguration<SupplementPlanItem>
{
    public void Configure(EntityTypeBuilder<SupplementPlanItem> builder)
    {
        builder.ToTable("SupplementPlanItems");

        builder.Property(e => e.SupplementName).HasMaxLength(160).IsRequired();
        builder.Property(e => e.Dosage).HasMaxLength(120).IsRequired();
        builder.Property(e => e.DaysOfWeekMask)
            .HasConversion(mask => (int)mask, value => (DaysOfWeekSet)value);

        builder.HasIndex(e => new { e.PlanId, e.Order, e.TimeOfDay });

        builder.HasOne(e => e.Plan)
            .WithMany(e => e.Items)
            .HasForeignKey(e => e.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
