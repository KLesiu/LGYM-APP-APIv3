namespace LgymApi.ArchitectureTests;

internal static class PersistenceIdentityContract
{
    internal const string DbContextTypeName = "AppDbContext";
    internal const string DbContextSourcePath = "LgymApi.Infrastructure/Data/AppDbContext.cs";
    internal const string MigrationRoot = "LgymApi.Infrastructure/Migrations";
    internal const string SnapshotTypeName = "AppDbContextModelSnapshot";
    internal const string SnapshotSourcePath = "LgymApi.Infrastructure/Migrations/AppDbContextModelSnapshot.cs";
    internal const string RegistrarSourcePath = "LgymApi.Infrastructure/Data/Configurations/AppDbContextEntityTypeConfigurationRegistrar.cs";

    internal static IReadOnlyList<PersistedDbSetIdentity> DbSets { get; } =
    [
        DbSet("Users", "User"),
        DbSet("Plans", "Plan"),
        DbSet("PlanDays", "PlanDay"),
        DbSet("PlanDayExercises", "PlanDayExercise"),
        DbSet("Exercises", "Exercise"),
        DbSet("ExerciseTranslations", "ExerciseTranslation"),
        DbSet("Trainings", "Training"),
        DbSet("TrainingExerciseScores", "TrainingExerciseScore"),
        DbSet("ExerciseScores", "ExerciseScore"),
        DbSet("Measurements", "Measurement"),
        DbSet("MainRecords", "MainRecord"),
        DbSet("Gyms", "Gym"),
        DbSet("Addresses", "Address"),
        DbSet("EloRegistries", "EloRegistry"),
        DbSet("AppConfigs", "AppConfig"),
        DbSet("Roles", "Role"),
        DbSet("UserRoles", "UserRole"),
        DbSet("RoleClaims", "RoleClaim"),
        DbSet("TrainerInvitations", "TrainerInvitation"),
        DbSet("TrainerTraineeLinks", "TrainerTraineeLink"),
        DbSet("NotificationMessages", "NotificationMessage"),
        DbSet("EmailNotificationSubscriptions", "EmailNotificationSubscription"),
        DbSet("ReportTemplates", "ReportTemplate"),
        DbSet("ReportTemplateFields", "ReportTemplateField"),
        DbSet("RecurringReportAssignments", "RecurringReportAssignment"),
        DbSet("ReportRequests", "ReportRequest"),
        DbSet("ReportSubmissions", "ReportSubmission"),
        DbSet("SupplementPlans", "SupplementPlan"),
        DbSet("SupplementPlanItems", "SupplementPlanItem"),
        DbSet("SupplementIntakeLogs", "SupplementIntakeLog"),
        DbSet("DietPlans", "DietPlan"),
        DbSet("DietMeals", "DietMeal"),
        DbSet("DietPlanHistories", "DietPlanHistory"),
        DbSet("TraineeNotes", "TraineeNote"),
        DbSet("TraineeNoteHistories", "TraineeNoteHistory"),
        DbSet("CommandEnvelopes", "CommandEnvelope"),
        DbSet("ActionExecutionLogs", "ActionExecutionLog"),
        DbSet("ApiIdempotencyRecords", "ApiIdempotencyRecord"),
        DbSet("UserTutorialStepProgresses", "UserTutorialStepProgress"),
        DbSet("UserTutorialProgresses", "UserTutorialProgress"),
        DbSet("PasswordResetTokens", "PasswordResetToken"),
        DbSet("InAppNotifications", "InAppNotification"),
        DbSet("UserSessions", "UserSession"),
        DbSet("PushInstallations", "PushInstallation"),
        DbSet("PushNotificationMessages", "PushNotificationMessage"),
        DbSet("UserExternalLogins", "UserExternalLogin"),
        DbSet("Photos", "Photo"),
        DbSet("PhotoUploadSessions", "PhotoUploadSession")
    ];

    internal static IReadOnlyList<string> RegistrarConfigurationTypes { get; } =
    [
        "UserEntityTypeConfiguration",
        "RoleEntityTypeConfiguration",
        "UserRoleEntityTypeConfiguration",
        "RoleClaimEntityTypeConfiguration",
        "PasswordResetTokenEntityTypeConfiguration",
        "UserExternalLoginEntityTypeConfiguration",
        "PlanEntityTypeConfiguration",
        "PlanDayEntityTypeConfiguration",
        "PlanDayExerciseEntityTypeConfiguration",
        "ExerciseEntityTypeConfiguration",
        "ExerciseTranslationEntityTypeConfiguration",
        "TrainingEntityTypeConfiguration",
        "TrainingExerciseScoreEntityTypeConfiguration",
        "ExerciseScoreEntityTypeConfiguration",
        "MeasurementEntityTypeConfiguration",
        "MainRecordEntityTypeConfiguration",
        "GymEntityTypeConfiguration",
        "AddressEntityTypeConfiguration",
        "EloRegistryEntityTypeConfiguration",
        "AppConfigEntityTypeConfiguration",
        "TrainerInvitationEntityTypeConfiguration",
        "TrainerTraineeLinkEntityTypeConfiguration",
        "TraineeNoteEntityTypeConfiguration",
        "TraineeNoteHistoryEntityTypeConfiguration",
        "PushInstallationEntityTypeConfiguration",
        "PushNotificationMessageEntityTypeConfiguration",
        "NotificationMessageEntityTypeConfiguration",
        "EmailNotificationSubscriptionEntityTypeConfiguration",
        "InAppNotificationEntityTypeConfiguration",
        "UserTutorialProgressEntityTypeConfiguration",
        "UserTutorialStepProgressEntityTypeConfiguration",
        "ApiIdempotencyRecordEntityTypeConfiguration",
        "UserSessionEntityTypeConfiguration",
        "CommandEnvelopeEntityTypeConfiguration",
        "ActionExecutionLogEntityTypeConfiguration",
        "SupplementPlanEntityTypeConfiguration",
        "SupplementPlanItemEntityTypeConfiguration",
        "SupplementIntakeLogEntityTypeConfiguration",
        "DietPlanEntityTypeConfiguration",
        "DietMealEntityTypeConfiguration",
        "DietPlanHistoryEntityTypeConfiguration",
        "ReportTemplateEntityTypeConfiguration",
        "ReportTemplateFieldEntityTypeConfiguration",
        "ReportRequestEntityTypeConfiguration",
        "ReportSubmissionEntityTypeConfiguration",
        "RecurringReportAssignmentEntityTypeConfiguration",
        "PhotoEntityTypeConfiguration",
        "PhotoUploadSessionEntityTypeConfiguration"
    ];

    private static PersistedDbSetIdentity DbSet(string propertyName, string entityTypeName)
    {
        return new PersistedDbSetIdentity(propertyName, $"LgymApi.Domain.Entities.{entityTypeName}");
    }
}
