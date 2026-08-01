using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public sealed record CreateManagedPlanCommand(
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    string Name)
{
    internal CreateManagedPlanCommand(Id<UserEntity> trainerId, Id<UserEntity> traineeId, string name)
        : this(trainerId.Rebind<AccountReference>(), traineeId.Rebind<AccountReference>(), name)
    {
    }
}
