using LgymApi.Domain.Entities;
using LgymApi.TrainingPlanning.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.WorkoutProgress;

internal sealed class ExerciseTranslationEntityTypeConfiguration : IEntityTypeConfiguration<ExerciseTranslation>
{
    public void Configure(EntityTypeBuilder<ExerciseTranslation> builder)
    {
        builder.ToTable("ExerciseTranslations");

        builder.Property(e => e.Culture).HasMaxLength(16).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();

        builder.HasIndex(e => new { e.ExerciseId, e.Culture })
            .IsUnique()
            .HasFilter(TrainingPlanningConfigurationFilters.ActiveRowsFilter);

        builder.HasOne(e => e.Exercise)
            .WithMany(e => e.Translations)
            .HasForeignKey(e => e.ExerciseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
