using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public sealed record GetActiveAssignedPlanQuery(Id<AccountReference> TraineeId)
{
    internal GetActiveAssignedPlanQuery(Id<UserEntity> traineeId)
        : this(traineeId.Rebind<AccountReference>())
    {
    }
}
