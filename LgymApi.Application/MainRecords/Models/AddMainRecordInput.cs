using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.MainRecords.Models;

public sealed record AddMainRecordInput(
    Id<LgymApi.Identity.Contracts.AccountReference> UserId,
    Id<LgymApi.Domain.Entities.Exercise> ExerciseId,
    double Weight,
    WeightUnits Unit,
    DateTime Date);
