using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Plan.GetPlansList;

public sealed record GetPlansListQuery(Id<AccountReference> CurrentUserId, Id<AccountReference> RouteUserId)
{
    internal GetPlansListQuery(Id<UserEntity> currentUserId, Id<UserEntity> routeUserId)
        : this(currentUserId.Rebind<AccountReference>(), routeUserId.Rebind<AccountReference>())
    {
    }
}
