using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.WorkoutProgress.Persistence;

public sealed record WorkoutExercisePersistenceModel(
    Id<LgymApi.Domain.Entities.Exercise> Id,
    Id<AccountReference>? OwnerId,
    string Name,
    BodyParts BodyPart,
    ExerciseEloFormula EloFormula,
    string? Description,
    string? Image,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkoutExerciseWriteModel(
    Id<LgymApi.Domain.Entities.Exercise> Id,
    Id<AccountReference>? OwnerId,
    string Name,
    BodyParts BodyPart,
    ExerciseEloFormula EloFormula,
    string? Description,
    string? Image,
    bool IsDeleted);

public sealed record WorkoutGymPersistenceModel(
    Id<LgymApi.Domain.Entities.Gym> Id,
    Id<AccountReference> OwnerId,
    string Name,
    Id<LgymApi.Domain.Entities.Address>? AddressId,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkoutGymWriteModel(
    Id<LgymApi.Domain.Entities.Gym> Id,
    Id<AccountReference> OwnerId,
    string Name,
    Id<LgymApi.Domain.Entities.Address>? AddressId,
    bool IsDeleted);

public sealed record WorkoutTrainingPersistenceModel(
    Id<LgymApi.Domain.Entities.Training> Id,
    Id<AccountReference> AccountId,
    Id<PlanDayReference> TypePlanDayId,
    Id<LgymApi.Domain.Entities.Gym> GymId,
    DateTimeOffset CreatedAt,
    WorkoutGymPersistenceModel? Gym);

public sealed record WorkoutTrainingWriteModel(
    Id<LgymApi.Domain.Entities.Training> Id,
    Id<AccountReference> AccountId,
    Id<PlanDayReference> TypePlanDayId,
    Id<LgymApi.Domain.Entities.Gym> GymId,
    DateTimeOffset CreatedAt);

public sealed record WorkoutExerciseScorePersistenceModel(
    Id<LgymApi.Domain.Entities.ExerciseScore> Id,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    Id<AccountReference> AccountId,
    double Reps,
    int Series,
    double Weight,
    WeightUnits Unit,
    Id<LgymApi.Domain.Entities.Training> TrainingId,
    int Order,
    DateTimeOffset CreatedAt,
    WorkoutExercisePersistenceModel? Exercise,
    WorkoutTrainingPersistenceModel? Training);

public sealed record WorkoutExerciseScoreWriteModel(
    Id<LgymApi.Domain.Entities.ExerciseScore> Id,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    Id<AccountReference> AccountId,
    double Reps,
    int Series,
    double Weight,
    WeightUnits Unit,
    Id<LgymApi.Domain.Entities.Training> TrainingId,
    int Order);

public sealed record WorkoutTrainingExerciseScorePersistenceModel(
    Id<LgymApi.Domain.Entities.TrainingExerciseScore> Id,
    Id<LgymApi.Domain.Entities.Training> TrainingId,
    Id<LgymApi.Domain.Entities.ExerciseScore> ExerciseScoreId,
    int Order);

public sealed record WorkoutMeasurementPersistenceModel(
    Id<LgymApi.Domain.Entities.Measurement> Id,
    Id<AccountReference> AccountId,
    BodyParts BodyPart,
    string Unit,
    double Value,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkoutMeasurementWriteModel(
    Id<LgymApi.Domain.Entities.Measurement> Id,
    Id<AccountReference> AccountId,
    BodyParts BodyPart,
    string Unit,
    double Value,
    DateTimeOffset? CreatedAt = null);

public sealed record WorkoutMainRecordPersistenceModel(
    Id<LgymApi.Domain.Entities.MainRecord> Id,
    Id<AccountReference> AccountId,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    double Weight,
    WeightUnits Unit,
    DateTimeOffset Date);

public sealed record WorkoutMainRecordWriteModel(
    Id<LgymApi.Domain.Entities.MainRecord> Id,
    Id<AccountReference> AccountId,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    double Weight,
    WeightUnits Unit,
    DateTimeOffset Date);

public sealed record WorkoutEloPersistenceModel(
    Id<LgymApi.Domain.Entities.EloRegistry> Id,
    Id<AccountReference> AccountId,
    DateTimeOffset Date,
    int Elo,
    Id<LgymApi.Domain.Entities.Training>? TrainingId);

public sealed record WorkoutEloWriteModel(
    Id<LgymApi.Domain.Entities.EloRegistry> Id,
    Id<AccountReference> AccountId,
    DateTimeOffset Date,
    int Elo,
    Id<LgymApi.Domain.Entities.Training>? TrainingId);
