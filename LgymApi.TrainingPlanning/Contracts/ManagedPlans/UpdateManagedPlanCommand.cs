using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public sealed record UpdateManagedPlanCommand(
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    Id<PlanReference> PlanId,
    string Name)
{
    internal UpdateManagedPlanCommand(Id<UserEntity> trainerId, Id<UserEntity> traineeId, Id<PlanEntity> planId, string name)
        : this(trainerId.Rebind<AccountReference>(), traineeId.Rebind<AccountReference>(), planId.Rebind<PlanReference>(), name)
    {
    }
}
