using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.TrainingPlanning.Contracts.PlanDay;

public sealed record PlanDayReferenceReadModel(
    Id<PlanDayReference> PlanDayId,
    Id<PlanReference> PlanId,
    string Name,
    bool Exists,
    bool IsDeleted);
