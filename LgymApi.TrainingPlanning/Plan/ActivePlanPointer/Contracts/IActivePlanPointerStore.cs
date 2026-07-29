using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;

internal interface IActivePlanPointerStore
{
    Task<Id<PlanEntity>?> GetActivePlanIdAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default);

    Task StageActivePlanIdAsync(
        Id<UserEntity> userId,
        Id<PlanEntity>? planId,
        CancellationToken cancellationToken = default);

    Task<Id<PlanEntity>?> GetActivePlanIdAsync(Id<AccountReference> userId, CancellationToken cancellationToken = default)
        => GetActivePlanIdAsync(userId.Rebind<UserEntity>(), cancellationToken);

    Task StageActivePlanIdAsync(Id<AccountReference> userId, Id<PlanEntity>? planId, CancellationToken cancellationToken = default)
        => StageActivePlanIdAsync(userId.Rebind<UserEntity>(), planId, cancellationToken);

}
