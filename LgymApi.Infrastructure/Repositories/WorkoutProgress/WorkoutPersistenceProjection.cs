using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Infrastructure.Repositories.WorkoutProgress;

internal static class WorkoutPersistenceProjection
{
    public static WorkoutExercisePersistenceModel Exercise(Exercise entity) => new(
        entity.Id,
        entity.UserId.HasValue ? WorkoutPersistenceAccountIds.ToContract(entity.UserId.Value) : null,
        entity.Name,
        entity.BodyPart,
        entity.EloFormula,
        entity.Description,
        entity.Image,
        entity.IsDeleted,
        entity.CreatedAt,
        entity.UpdatedAt);

    public static WorkoutGymPersistenceModel Gym(Gym entity) => new(
        entity.Id,
        WorkoutPersistenceAccountIds.ToContract(entity.UserId),
        entity.Name,
        entity.AddressId,
        entity.IsDeleted,
        entity.CreatedAt,
        entity.UpdatedAt);

    public static WorkoutTrainingPersistenceModel Training(Training entity) => new(
        entity.Id,
        WorkoutPersistenceAccountIds.ToContract(entity.UserId),
        entity.TypePlanDayId.Rebind<PlanDayReference>(),
        entity.GymId,
        entity.CreatedAt,
        entity.Gym is null ? null : Gym(entity.Gym));

    public static WorkoutExerciseScorePersistenceModel ExerciseScore(ExerciseScore entity) => new(
        entity.Id,
        entity.ExerciseId,
        WorkoutPersistenceAccountIds.ToContract(entity.UserId),
        entity.Reps,
        entity.Series,
        entity.Weight.Value,
        entity.Weight.Unit,
        entity.TrainingId,
        entity.Order,
        entity.CreatedAt,
        entity.Exercise is null ? null : Exercise(entity.Exercise),
        entity.Training is null ? null : Training(entity.Training));

    public static WorkoutMeasurementPersistenceModel Measurement(Measurement entity) => new(
        entity.Id,
        WorkoutPersistenceAccountIds.ToContract(entity.UserId),
        entity.BodyPart,
        entity.Unit,
        entity.Value,
        entity.CreatedAt,
        entity.UpdatedAt);

    public static WorkoutMainRecordPersistenceModel MainRecord(MainRecord entity) => new(
        entity.Id,
        WorkoutPersistenceAccountIds.ToContract(entity.UserId),
        entity.ExerciseId,
        entity.Weight.Value,
        entity.Weight.Unit,
        entity.Date);

    public static WorkoutEloPersistenceModel Elo(EloRegistry entity) => new(
        entity.Id,
        WorkoutPersistenceAccountIds.ToContract(entity.UserId),
        entity.Date,
        entity.Elo,
        entity.TrainingId);
}
