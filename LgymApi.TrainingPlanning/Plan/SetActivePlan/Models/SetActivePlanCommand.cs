using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Plan.SetActivePlan;

public sealed record SetActivePlanCommand(
    Id<AccountReference> CurrentUserId,
    Id<AccountReference> RouteUserId,
    Id<PlanReference> PlanId)
{
    internal SetActivePlanCommand(Id<UserEntity> currentUserId, Id<UserEntity> routeUserId, Id<PlanEntity> planId)
        : this(currentUserId.Rebind<AccountReference>(), routeUserId.Rebind<AccountReference>(), planId.Rebind<PlanReference>())
    {
    }
}
