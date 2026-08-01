using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.WorkoutProgress;

internal sealed class TrainingExerciseScoreEntityTypeConfiguration : IEntityTypeConfiguration<TrainingExerciseScore>
{
    public void Configure(EntityTypeBuilder<TrainingExerciseScore> builder)
    {
        builder.ToTable("TrainingExerciseScores");

        builder.HasOne(t => t.Training)
            .WithMany(t => t.Exercises)
            .HasForeignKey(t => t.TrainingId);

        builder.HasOne(t => t.ExerciseScore)
            .WithMany()
            .HasForeignKey(t => t.ExerciseScoreId);
    }
}
