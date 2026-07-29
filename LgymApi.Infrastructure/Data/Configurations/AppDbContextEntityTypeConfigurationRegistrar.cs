using System;
using LgymApi.Infrastructure.Data.Configurations.Coaching;
using LgymApi.Infrastructure.Data.Configurations.Nutrition;
using LgymApi.Infrastructure.Data.Configurations.Platform;
using LgymApi.Infrastructure.Data.Configurations.ReferenceData;
using LgymApi.Infrastructure.Data.Configurations.Reporting;
using LgymApi.Infrastructure.Data.Configurations.WorkoutProgress;
using LgymApi.Identity.Persistence;
using LgymApi.Notifications.Persistence;
using LgymApi.Platform.Persistence;
using LgymApi.TrainingPlanning.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Data.Configurations;

internal static class AppDbContextEntityTypeConfigurationRegistrar
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        IdentityModelConfigurationRegistrar.Apply(modelBuilder);
        TrainingPlanningModelConfigurationRegistrar.Apply(modelBuilder);
        ApplyWorkoutProgress(modelBuilder);
        PlatformModelConfigurationRegistrar.ApplyReferenceData(modelBuilder, ApplyPlatformReferenceData);
        ApplyCoaching(modelBuilder);
        NotificationsModelConfigurationRegistrar.Apply(modelBuilder);
        PlatformModelConfigurationRegistrar.ApplyReliability(modelBuilder, ApplyPlatformReliability);
        ApplyNutrition(modelBuilder);
        ApplyReporting(modelBuilder);
    }

    private static void ApplyWorkoutProgress(ModelBuilder modelBuilder)
    {
        Register(modelBuilder, new ExerciseEntityTypeConfiguration());
        Register(modelBuilder, new ExerciseTranslationEntityTypeConfiguration());
        Register(modelBuilder, new TrainingEntityTypeConfiguration());
        Register(modelBuilder, new TrainingExerciseScoreEntityTypeConfiguration());
        Register(modelBuilder, new ExerciseScoreEntityTypeConfiguration());
        Register(modelBuilder, new MeasurementEntityTypeConfiguration());
        Register(modelBuilder, new MainRecordEntityTypeConfiguration());
        Register(modelBuilder, new GymEntityTypeConfiguration());
        Register(modelBuilder, new AddressEntityTypeConfiguration());
        Register(modelBuilder, new EloRegistryEntityTypeConfiguration());
    }

    private static void ApplyPlatformReferenceData(ModelBuilder modelBuilder)
    {
        Register(modelBuilder, new AppConfigEntityTypeConfiguration());
    }

    private static void ApplyCoaching(ModelBuilder modelBuilder)
    {
        Register(modelBuilder, new TrainerInvitationEntityTypeConfiguration());
        Register(modelBuilder, new TrainerTraineeLinkEntityTypeConfiguration());
        Register(modelBuilder, new TraineeNoteEntityTypeConfiguration());
        Register(modelBuilder, new TraineeNoteHistoryEntityTypeConfiguration());
    }

    private static void ApplyPlatformReliability(ModelBuilder modelBuilder)
    {
        Register(modelBuilder, new ApiIdempotencyRecordEntityTypeConfiguration());
        Register(modelBuilder, new CommandEnvelopeEntityTypeConfiguration());
        Register(modelBuilder, new ActionExecutionLogEntityTypeConfiguration());
    }

    private static void ApplyNutrition(ModelBuilder modelBuilder)
    {
        Register(modelBuilder, new SupplementPlanEntityTypeConfiguration());
        Register(modelBuilder, new SupplementPlanItemEntityTypeConfiguration());
        Register(modelBuilder, new SupplementIntakeLogEntityTypeConfiguration());
        Register(modelBuilder, new DietPlanEntityTypeConfiguration());
        Register(modelBuilder, new DietMealEntityTypeConfiguration());
        Register(modelBuilder, new DietPlanHistoryEntityTypeConfiguration());
    }

    private static void ApplyReporting(ModelBuilder modelBuilder)
    {
        Register(modelBuilder, new ReportTemplateEntityTypeConfiguration());
        Register(modelBuilder, new ReportTemplateFieldEntityTypeConfiguration());
        Register(modelBuilder, new ReportRequestEntityTypeConfiguration());
        Register(modelBuilder, new ReportSubmissionEntityTypeConfiguration());
        Register(modelBuilder, new RecurringReportAssignmentEntityTypeConfiguration());
        Register(modelBuilder, new PhotoEntityTypeConfiguration());
        Register(modelBuilder, new PhotoUploadSessionEntityTypeConfiguration());
    }

    private static void Register<TEntity>(ModelBuilder modelBuilder, IEntityTypeConfiguration<TEntity> configuration)
        where TEntity : class
    {
        modelBuilder.ApplyConfiguration(configuration);
    }
}
