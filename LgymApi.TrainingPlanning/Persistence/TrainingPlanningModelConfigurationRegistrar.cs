using LgymApi.TrainingPlanning.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.TrainingPlanning.Persistence;

internal static class TrainingPlanningModelConfigurationRegistrar
{
    internal static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PlanEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new PlanDayEntityTypeConfiguration());
        modelBuilder.ApplyConfiguration(new PlanDayExerciseEntityTypeConfiguration());
    }
}
