using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public sealed record ManagedPlanReadModel(
    Id<PlanReference> Id,
    string Name,
    bool IsActive,
    DateTimeOffset CreatedAt)
{
    internal ManagedPlanReadModel(Id<PlanEntity> id, string name, bool isActive, DateTimeOffset createdAt)
        : this(id.Rebind<PlanReference>(), name, isActive, createdAt)
    {
    }
}
