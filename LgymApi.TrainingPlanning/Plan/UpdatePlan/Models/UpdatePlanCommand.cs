using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Plan.UpdatePlan;

public sealed record UpdatePlanCommand(
    Id<AccountReference> CurrentUserId,
    Id<AccountReference> RouteUserId,
    Id<PlanReference> PlanId,
    string Name)
{
    internal UpdatePlanCommand(Id<UserEntity> currentUserId, Id<UserEntity> routeUserId, Id<PlanEntity> planId, string name)
        : this(currentUserId.Rebind<AccountReference>(), routeUserId.Rebind<AccountReference>(), planId.Rebind<PlanReference>(), name)
    {
    }
}
