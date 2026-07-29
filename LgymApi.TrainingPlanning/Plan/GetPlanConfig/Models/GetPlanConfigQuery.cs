using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Plan.GetPlanConfig;

public sealed record GetPlanConfigQuery(Id<AccountReference> CurrentUserId, Id<AccountReference> RouteUserId)
{
    internal GetPlanConfigQuery(Id<UserEntity> currentUserId, Id<UserEntity> routeUserId)
        : this(currentUserId.Rebind<AccountReference>(), routeUserId.Rebind<AccountReference>())
    {
    }
}
