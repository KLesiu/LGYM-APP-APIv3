using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Nutrition;

internal sealed class SupplementPlanEntityTypeConfiguration : IEntityTypeConfiguration<SupplementPlan>
{
    public void Configure(EntityTypeBuilder<SupplementPlan> builder)
    {
        builder.ToTable("SupplementPlans");

        builder.Property(e => e.Name).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(1000);

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
