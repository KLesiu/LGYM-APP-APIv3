using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.WorkoutProgress;

internal sealed class TrainingEntityTypeConfiguration : IEntityTypeConfiguration<Training>
{
    public void Configure(EntityTypeBuilder<Training> builder)
    {
        builder.ToTable("Trainings");

        builder.HasOne(t => t.User)
            .WithMany(u => u.Trainings)
            .HasForeignKey(t => t.UserId);

        builder.HasOne(t => t.PlanDay)
            .WithMany()
            .HasForeignKey(t => t.TypePlanDayId);

        builder.HasOne(t => t.Gym)
            .WithMany(g => g.Trainings)
            .HasForeignKey(t => t.GymId);
    }
}
