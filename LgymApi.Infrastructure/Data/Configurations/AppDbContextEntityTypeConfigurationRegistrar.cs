using System;
using LgymApi.Infrastructure.Data.Configurations.Coaching;
using LgymApi.Infrastructure.Data.Configurations.Identity;
using LgymApi.Infrastructure.Data.Configurations.Nutrition;
using LgymApi.Infrastructure.Data.Configurations.Notifications;
using LgymApi.Infrastructure.Data.Configurations.Platform;
using LgymApi.Infrastructure.Data.Configurations.ReferenceData;
using LgymApi.Infrastructure.Data.Configurations.Reporting;
using LgymApi.Infrastructure.Data.Configurations.TrainingPlanning;
using LgymApi.Infrastructure.Data.Configurations.WorkoutProgress;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Data.Configurations;

internal static class AppDbContextEntityTypeConfigurationRegistrar
{
    private static readonly Action<ModelBuilder>[] Registrations =
    {
        Register(new UserEntityTypeConfiguration()),
        Register(new RoleEntityTypeConfiguration()),
        Register(new UserRoleEntityTypeConfiguration()),
        Register(new RoleClaimEntityTypeConfiguration()),
        Register(new PasswordResetTokenEntityTypeConfiguration()),
        Register(new UserExternalLoginEntityTypeConfiguration()),
        Register(new PlanEntityTypeConfiguration()),
        Register(new PlanDayEntityTypeConfiguration()),
        Register(new PlanDayExerciseEntityTypeConfiguration()),
        Register(new ExerciseEntityTypeConfiguration()),
        Register(new ExerciseTranslationEntityTypeConfiguration()),
        Register(new TrainingEntityTypeConfiguration()),
        Register(new TrainingExerciseScoreEntityTypeConfiguration()),
        Register(new ExerciseScoreEntityTypeConfiguration()),
        Register(new MeasurementEntityTypeConfiguration()),
        Register(new MainRecordEntityTypeConfiguration()),
        Register(new GymEntityTypeConfiguration()),
        Register(new AddressEntityTypeConfiguration()),
        Register(new EloRegistryEntityTypeConfiguration()),
        Register(new AppConfigEntityTypeConfiguration()),
        Register(new TrainerInvitationEntityTypeConfiguration()),
        Register(new TrainerTraineeLinkEntityTypeConfiguration()),
        Register(new TraineeNoteEntityTypeConfiguration()),
        Register(new TraineeNoteHistoryEntityTypeConfiguration()),
        Register(new PushInstallationEntityTypeConfiguration()),
        Register(new PushNotificationMessageEntityTypeConfiguration()),
        Register(new NotificationMessageEntityTypeConfiguration()),
        Register(new EmailNotificationSubscriptionEntityTypeConfiguration()),
        Register(new InAppNotificationEntityTypeConfiguration()),
        Register(new UserTutorialProgressEntityTypeConfiguration()),
        Register(new UserTutorialStepProgressEntityTypeConfiguration()),
        Register(new ApiIdempotencyRecordEntityTypeConfiguration()),
        Register(new UserSessionEntityTypeConfiguration()),
        Register(new CommandEnvelopeEntityTypeConfiguration()),
        Register(new ActionExecutionLogEntityTypeConfiguration()),
        Register(new SupplementPlanEntityTypeConfiguration()),
        Register(new SupplementPlanItemEntityTypeConfiguration()),
        Register(new SupplementIntakeLogEntityTypeConfiguration()),
        Register(new DietPlanEntityTypeConfiguration()),
        Register(new DietMealEntityTypeConfiguration()),
        Register(new DietPlanHistoryEntityTypeConfiguration()),
        Register(new ReportTemplateEntityTypeConfiguration()),
        Register(new ReportTemplateFieldEntityTypeConfiguration()),
        Register(new ReportRequestEntityTypeConfiguration()),
        Register(new ReportSubmissionEntityTypeConfiguration()),
        Register(new RecurringReportAssignmentEntityTypeConfiguration()),
        Register(new PhotoEntityTypeConfiguration()),
        Register(new PhotoUploadSessionEntityTypeConfiguration()),
    };

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var registration in Registrations)
        {
            registration(modelBuilder);
        }
    }

    private static Action<ModelBuilder> Register<TEntity>(IEntityTypeConfiguration<TEntity> configuration)
        where TEntity : class
    {
        return modelBuilder => modelBuilder.ApplyConfiguration(configuration);
    }
}
