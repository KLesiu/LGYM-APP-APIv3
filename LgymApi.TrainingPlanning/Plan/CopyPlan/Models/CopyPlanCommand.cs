using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Plan.CopyPlan;

public sealed record CopyPlanCommand(Id<AccountReference> CurrentUserId, string ShareCode)
{
    internal CopyPlanCommand(Id<UserEntity> currentUserId, string shareCode)
        : this(currentUserId.Rebind<AccountReference>(), shareCode)
    {
    }
}
