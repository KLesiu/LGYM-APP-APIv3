using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.ProgressData.Models;

public sealed record ExerciseScoreChartPoint(
    string Id,
    double Value,
    string Date,
    string ExerciseName,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId);

public sealed record MeasurementReadModel(
    Id<LgymApi.Domain.Entities.Measurement> Id,
    Id<LgymApi.Identity.Contracts.AccountReference> UserId,
    BodyParts BodyPart,
    MeasurementUnits Unit,
    double Value,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MeasurementTrendReadModel(
    BodyParts BodyPart,
    MeasurementUnits Unit,
    double? StartValue,
    double? CurrentValue,
    double? Change,
    double? ChangePercentage,
    double? FirstMeasurementValue,
    DateTimeOffset? FirstMeasurementDate,
    double? LastMeasurementValue,
    DateTimeOffset? LastMeasurementDate,
    double? Difference,
    string Direction,
    int Points);

public sealed record MainRecordReadModel(
    Id<LgymApi.Domain.Entities.MainRecord> Id,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    double Weight,
    WeightUnits Unit,
    DateTime Date);

public sealed record ProgressExerciseReadModel(
    Id<LgymApi.Domain.Entities.Exercise> Id,
    string Name,
    Id<LgymApi.Identity.Contracts.AccountReference>? UserId,
    BodyParts BodyPart,
    ExerciseEloFormula? EloFormula,
    string? Description,
    string? Image);

public sealed record WorkoutExerciseScoreReadModel(
    Id<LgymApi.Domain.Entities.ExerciseScore> Id,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    double Weight,
    WeightUnits Unit,
    double Reps,
    int Series,
    WorkoutScoreTrainingReadModel? Training);

public sealed record WorkoutScoreTrainingReadModel(
    Id<LgymApi.Domain.Entities.Training> Id,
    Id<LgymApi.Domain.Entities.Gym> GymId,
    string? GymName,
    DateTimeOffset CreatedAt);

public sealed record MainRecordBestReadModel(
    MainRecordReadModel Record,
    ProgressExerciseReadModel Exercise);

public sealed record PossibleRecordReadModel(
    double Weight,
    double Reps,
    WeightUnits Unit,
    DateTime Date);

public sealed record EloChartPoint(
    Id<LgymApi.Domain.Entities.EloRegistry> Id,
    int Value,
    string Date);
