using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.ProgressData.Models;

public sealed record MeasurementWriteModel(
    BodyParts BodyPart,
    MeasurementUnits Unit,
    double Value);

public sealed record MainRecordCreateWriteModel(
    Id<LgymApi.Identity.Contracts.AccountReference> UserId,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    double Weight,
    WeightUnits Unit,
    DateTime Date);

public sealed record MainRecordUpdateWriteModel(
    Id<LgymApi.Identity.Contracts.AccountReference> RouteUserId,
    Id<LgymApi.Identity.Contracts.AccountReference> CurrentUserId,
    Id<LgymApi.Domain.Entities.MainRecord> RecordId,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    double Weight,
    WeightUnits Unit,
    DateTime Date);
