using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.TrainingPlanning.Persistence.Configurations;

internal sealed class PlanDayEntityTypeConfiguration : IEntityTypeConfiguration<PlanDay>
{
    public void Configure(EntityTypeBuilder<PlanDay> builder)
    {
        builder.ToTable("PlanDays");

        builder.HasOne(planDay => planDay.Plan)
            .WithMany(plan => plan.PlanDays)
            .HasForeignKey(planDay => planDay.PlanId);
    }
}
