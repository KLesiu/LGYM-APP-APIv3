using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public sealed record GetManagedPlansQuery(Id<AccountReference> TraineeId)
{
    internal GetManagedPlansQuery(Id<UserEntity> traineeId)
        : this(traineeId.Rebind<AccountReference>())
    {
    }
}
