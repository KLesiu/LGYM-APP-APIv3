using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Nutrition;

internal sealed class SupplementIntakeLogEntityTypeConfiguration : IEntityTypeConfiguration<SupplementIntakeLog>
{
    public void Configure(EntityTypeBuilder<SupplementIntakeLog> builder)
    {
        builder.ToTable("SupplementIntakeLogs");

        builder.HasIndex(e => new { e.TraineeId, e.PlanItemId, e.IntakeDate })
            .IsUnique()
            .HasFilter(NutritionConfigurationFilters.ActiveRowsFilter);

        builder.HasOne(e => e.Trainee)
            .WithMany()
            .HasForeignKey(e => e.TraineeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.PlanItem)
            .WithMany()
            .HasForeignKey(e => e.PlanItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
