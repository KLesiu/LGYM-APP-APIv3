using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.TrainingPlanning.Persistence.Configurations;

internal sealed class PlanDayExerciseEntityTypeConfiguration : IEntityTypeConfiguration<PlanDayExercise>
{
    public void Configure(EntityTypeBuilder<PlanDayExercise> builder)
    {
        builder.ToTable("PlanDayExercises");

        builder.HasOne(exercise => exercise.PlanDay)
            .WithMany(planDay => planDay.Exercises)
            .HasForeignKey(exercise => exercise.PlanDayId);

        builder.HasOne(exercise => exercise.Exercise)
            .WithMany()
            .HasForeignKey(exercise => exercise.ExerciseId);
    }
}
