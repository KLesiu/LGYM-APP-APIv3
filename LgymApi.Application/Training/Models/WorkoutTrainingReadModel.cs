using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.Features.Training.Models;

public sealed record WorkoutTrainingReadModel(
    Id<LgymApi.Domain.Entities.Training> Id,
    Id<PlanDayReference> TypePlanDayId,
    DateTimeOffset CreatedAt,
    PlanDayReferenceReadModel? PlanDay);
