using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Nutrition;

internal sealed class DietPlanEntityTypeConfiguration : IEntityTypeConfiguration<DietPlan>
{
    public void Configure(EntityTypeBuilder<DietPlan> builder)
    {
        builder.ToTable("DietPlans");

        builder.Property(e => e.Name).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(2000);

        builder.HasIndex(e => new { e.TrainerId, e.TraineeId, e.CreatedAt });
        builder.HasIndex(e => new { e.TraineeId, e.IsActive });

        builder.HasOne(e => e.Trainer)
            .WithMany()
            .HasForeignKey(e => e.TrainerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Trainee)
            .WithMany()
            .HasForeignKey(e => e.TraineeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
