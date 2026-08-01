using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.WorkoutProgress;

internal sealed class ExerciseScoreEntityTypeConfiguration : IEntityTypeConfiguration<ExerciseScore>
{
    public void Configure(EntityTypeBuilder<ExerciseScore> builder)
    {
        builder.ToTable("ExerciseScores");

        builder.Ignore(e => e.Weight);
        builder.Property(e => e.WeightValue).HasField("_weightValue").HasColumnName("Weight");
        builder.Property(e => e.Unit).HasField("_unit").HasConversion<string>();

        builder.HasOne(e => e.Exercise)
            .WithMany(e => e.ExerciseScores)
            .HasForeignKey(e => e.ExerciseId);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId);

        builder.HasOne(e => e.Training)
            .WithMany()
            .HasForeignKey(e => e.TrainingId);
    }
}
