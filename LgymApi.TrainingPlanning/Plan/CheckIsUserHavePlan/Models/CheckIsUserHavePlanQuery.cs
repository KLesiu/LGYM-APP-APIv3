using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Plan.CheckIsUserHavePlan;

public sealed record CheckIsUserHavePlanQuery(Id<AccountReference> CurrentUserId, Id<AccountReference> RouteUserId)
{
    internal CheckIsUserHavePlanQuery(Id<UserEntity> currentUserId, Id<UserEntity> routeUserId)
        : this(currentUserId.Rebind<AccountReference>(), routeUserId.Rebind<AccountReference>())
    {
    }
}
