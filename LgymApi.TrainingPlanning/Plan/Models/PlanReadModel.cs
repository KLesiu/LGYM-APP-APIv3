using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Plan.Models;

public sealed record PlanReadModel(
    Id<PlanReference> Id,
    Id<AccountReference> UserId,
    string Name,
    bool IsActive,
    string? ShareCode)
{
    internal PlanReadModel(Id<PlanEntity> id, Id<UserEntity> userId, string name, bool isActive, string? shareCode)
        : this(id.Rebind<PlanReference>(), userId.Rebind<AccountReference>(), name, isActive, shareCode)
    {
    }
}
