using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.MainRecords.Models;

public sealed record UpdateMainRecordInput(
    Id<LgymApi.Identity.Contracts.AccountReference> RouteUserId,
    Id<LgymApi.Identity.Contracts.AccountReference> CurrentUserId,
    Id<LgymApi.Domain.Entities.MainRecord> RecordId,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    double Weight,
    WeightUnits Unit,
    DateTime Date);
