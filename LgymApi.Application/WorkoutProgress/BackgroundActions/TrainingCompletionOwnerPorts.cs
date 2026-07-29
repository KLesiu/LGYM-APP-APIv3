namespace LgymApi.Application.WorkoutProgress.Contracts.BackgroundActions;

public sealed record TrainingCompletedExercisePreparation(string ExerciseId, string ExerciseName, int Series, double Reps, double Weight, string Unit);

public sealed record TrainingCompletedEmailPreparation(
    string UserId,
    string TrainingId,
    string RecipientEmail,
    string CultureName,
    string PreferredTimeZone,
    string PlanDayName,
    DateTimeOffset TrainingDate,
    IReadOnlyList<TrainingCompletedExercisePreparation> Exercises);

public interface ITrainingCompletedEmailPreparationPort
{
    Task<TrainingCompletedEmailPreparation?> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default);
}

public interface ITrainingMainRecordsUpdatePort
{
    Task UpdateAsync(string payloadJson, CancellationToken cancellationToken = default);
}
