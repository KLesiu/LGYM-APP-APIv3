using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public sealed record AssignManagedPlanCommand(
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    Id<PlanReference> PlanId)
{
    internal AssignManagedPlanCommand(Id<UserEntity> trainerId, Id<UserEntity> traineeId, Id<PlanEntity> planId)
        : this(trainerId.Rebind<AccountReference>(), traineeId.Rebind<AccountReference>(), planId.Rebind<PlanReference>())
    {
    }
}
