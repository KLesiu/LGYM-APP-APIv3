using LgymApi.Domain.Entities;

namespace LgymApi.ArchitectureTests;

internal sealed record PersistedEntityOwnership(Type EntityType, string Owner);

internal static class PersistedEntityOwnershipCatalog
{
    internal const string PlatformModuleName = "Platform / Reference Data";
    internal const string IdentityModuleName = "Identity & Accounts";
    internal const string NotificationsModuleName = "Notifications";
    internal const string ReportingModuleName = "Reporting";
    internal const string TrainingPlanningModuleName = "Training Planning";
    internal const string WorkoutProgressModuleName = "Workout & Progress";
    internal const string CoachingModuleName = "Coaching";
    internal const string NutritionModuleName = "Nutrition";

    internal static IReadOnlyList<string> CanonicalOwners { get; } =
    [
        PlatformModuleName,
        IdentityModuleName,
        NotificationsModuleName,
        ReportingModuleName,
        TrainingPlanningModuleName,
        WorkoutProgressModuleName,
        CoachingModuleName,
        NutritionModuleName
    ];

    internal static IReadOnlyList<PersistedEntityOwnership> Entries { get; } =
    [
        new(typeof(User), IdentityModuleName),
        new(typeof(Role), IdentityModuleName),
        new(typeof(UserRole), IdentityModuleName),
        new(typeof(RoleClaim), IdentityModuleName),
        new(typeof(PasswordResetToken), IdentityModuleName),
        new(typeof(UserExternalLogin), IdentityModuleName),
        new(typeof(UserSession), IdentityModuleName),
        new(typeof(UserTutorialProgress), IdentityModuleName),
        new(typeof(UserTutorialStepProgress), IdentityModuleName),
        new(typeof(NotificationMessage), NotificationsModuleName),
        new(typeof(EmailNotificationSubscription), NotificationsModuleName),
        new(typeof(PushInstallation), NotificationsModuleName),
        new(typeof(PushNotificationMessage), NotificationsModuleName),
        new(typeof(InAppNotification), NotificationsModuleName),
        new(typeof(ReportTemplate), ReportingModuleName),
        new(typeof(ReportTemplateField), ReportingModuleName),
        new(typeof(ReportRequest), ReportingModuleName),
        new(typeof(ReportSubmission), ReportingModuleName),
        new(typeof(RecurringReportAssignment), ReportingModuleName),
        new(typeof(Photo), ReportingModuleName),
        new(typeof(PhotoUploadSession), ReportingModuleName),
        new(typeof(Plan), TrainingPlanningModuleName),
        new(typeof(PlanDay), TrainingPlanningModuleName),
        new(typeof(PlanDayExercise), TrainingPlanningModuleName),
        new(typeof(Exercise), WorkoutProgressModuleName),
        new(typeof(ExerciseTranslation), WorkoutProgressModuleName),
        new(typeof(Training), WorkoutProgressModuleName),
        new(typeof(TrainingExerciseScore), WorkoutProgressModuleName),
        new(typeof(ExerciseScore), WorkoutProgressModuleName),
        new(typeof(Measurement), WorkoutProgressModuleName),
        new(typeof(MainRecord), WorkoutProgressModuleName),
        new(typeof(Gym), WorkoutProgressModuleName),
        new(typeof(Address), WorkoutProgressModuleName),
        new(typeof(EloRegistry), WorkoutProgressModuleName),
        new(typeof(TrainerInvitation), CoachingModuleName),
        new(typeof(TrainerTraineeLink), CoachingModuleName),
        new(typeof(TraineeNote), CoachingModuleName),
        new(typeof(TraineeNoteHistory), CoachingModuleName),
        new(typeof(DietPlan), NutritionModuleName),
        new(typeof(DietMeal), NutritionModuleName),
        new(typeof(DietPlanHistory), NutritionModuleName),
        new(typeof(SupplementPlan), NutritionModuleName),
        new(typeof(SupplementPlanItem), NutritionModuleName),
        new(typeof(SupplementIntakeLog), NutritionModuleName),
        new(typeof(AppConfig), PlatformModuleName),
        new(typeof(ActionExecutionLog), PlatformModuleName),
        new(typeof(CommandEnvelope), PlatformModuleName),
        new(typeof(ApiIdempotencyRecord), PlatformModuleName)
    ];

    private static readonly IReadOnlyDictionary<string, int> ExpectedEntityCountByOwner = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [IdentityModuleName] = 9,
        [NotificationsModuleName] = 5,
        [ReportingModuleName] = 7,
        [TrainingPlanningModuleName] = 3,
        [WorkoutProgressModuleName] = 10,
        [CoachingModuleName] = 4,
        [NutritionModuleName] = 6,
        [PlatformModuleName] = 4
    };

    internal static void Validate(
        IEnumerable<PersistedEntityOwnership> entries,
        IEnumerable<Type> persistedEntityTypes)
    {
        var catalogEntries = entries.ToList();
        var reflectedEntityTypes = persistedEntityTypes.ToHashSet();
        var duplicateEntityTypes = catalogEntries
            .GroupBy(entry => entry.EntityType)
            .Where(group => group.Count() > 1)
            .Select(group => GetDisplayName(group.Key))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (duplicateEntityTypes.Count != 0)
        {
            throw new InvalidOperationException(
                "Duplicate persisted entity catalog entries: " + string.Join(", ", duplicateEntityTypes));
        }

        var catalogEntityTypes = catalogEntries
            .Select(entry => entry.EntityType)
            .ToHashSet();
        var unknownEntityTypes = catalogEntityTypes
            .Except(reflectedEntityTypes)
            .Select(GetDisplayName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (unknownEntityTypes.Count != 0)
        {
            throw new InvalidOperationException(
                "Catalog entries that are not AppDbContext DbSet entity types: " + string.Join(", ", unknownEntityTypes));
        }

        var missingEntityTypes = reflectedEntityTypes
            .Except(catalogEntityTypes)
            .Select(GetDisplayName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        if (missingEntityTypes.Count != 0)
        {
            throw new InvalidOperationException(
                "Persisted DbSet entity types missing from catalog: " + string.Join(", ", missingEntityTypes));
        }

        var catalogOwners = catalogEntries
            .Select(entry => entry.Owner)
            .ToHashSet(StringComparer.Ordinal);
        var unexpectedOwners = catalogOwners
            .Except(CanonicalOwners, StringComparer.Ordinal)
            .OrderBy(owner => owner, StringComparer.Ordinal)
            .ToList();

        if (unexpectedOwners.Count != 0)
        {
            throw new InvalidOperationException(
                "Unexpected catalog owner modules: " + string.Join(", ", unexpectedOwners));
        }

        var missingOwners = CanonicalOwners
            .Except(catalogOwners, StringComparer.Ordinal)
            .OrderBy(owner => owner, StringComparer.Ordinal)
            .ToList();

        if (missingOwners.Count != 0)
        {
            throw new InvalidOperationException(
                "Canonical owner modules missing from catalog: " + string.Join(", ", missingOwners));
        }

        var incorrectOwnerTotals = ExpectedEntityCountByOwner
            .Where(expected => catalogEntries.Count(entry => entry.Owner == expected.Key) != expected.Value)
            .Select(expected => $"{expected.Key} expected {expected.Value}")
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToList();

        if (incorrectOwnerTotals.Count != 0)
        {
            throw new InvalidOperationException(
                "Catalog owner totals do not match the canonical roster: " + string.Join(", ", incorrectOwnerTotals));
        }
    }

    private static string GetDisplayName(Type entityType)
    {
        return entityType.FullName ?? entityType.Name;
    }
}
