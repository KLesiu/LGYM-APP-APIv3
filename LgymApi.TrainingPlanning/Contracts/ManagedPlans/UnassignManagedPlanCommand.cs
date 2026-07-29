using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public sealed record UnassignManagedPlanCommand(Id<AccountReference> TraineeId)
{
    internal UnassignManagedPlanCommand(Id<UserEntity> traineeId)
        : this(traineeId.Rebind<AccountReference>())
    {
    }
}
