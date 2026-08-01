using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Plan.DeletePlan;

public sealed record DeletePlanCommand(Id<AccountReference> CurrentUserId, Id<PlanReference> PlanId)
{
    internal DeletePlanCommand(Id<UserEntity> currentUserId, Id<PlanEntity> planId)
        : this(currentUserId.Rebind<AccountReference>(), planId.Rebind<PlanReference>())
    {
    }
}
