using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.Repositories;

internal interface IPlanRepository
{
    Task<Plan?> FindByIdAsync(Id<Plan> id, CancellationToken cancellationToken = default);
    Task<Plan?> FindActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<PlanReadModel?> FindActiveReadModelByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<Plan?> FindLastActiveByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<List<Plan>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<List<PlanReadModel>> GetReadModelsByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task AddAsync(Plan plan, CancellationToken cancellationToken = default);
    Task UpdateAsync(Plan plan, CancellationToken cancellationToken = default);
    Task SetActivePlanAsync(Id<User> userId, Id<Plan> planId, CancellationToken cancellationToken = default);
    Task ClearActivePlansAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<Plan?> FindByShareCodeAsync(string shareCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Id<PlanExerciseReference>>> GetPlanExerciseIdsAsync(Id<Plan> planId, CancellationToken cancellationToken = default);
    Task<Plan> ClonePlanAsync(
        Id<Plan> sourcePlanId,
        Id<User> userId,
        IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>> exerciseIdMap,
        bool isActive = true,
        CancellationToken cancellationToken = default);
    Task<string> GenerateShareCodeAsync(Id<Plan> planId, Id<User> userId, CancellationToken cancellationToken = default);

    Task<Plan?> FindByIdAsync(Id<PlanReference> id, CancellationToken cancellationToken = default)
        => FindByIdAsync(id.Rebind<Plan>(), cancellationToken);

    Task<Plan?> FindActiveByUserIdAsync(Id<AccountReference> userId, CancellationToken cancellationToken = default)
        => FindActiveByUserIdAsync(userId.Rebind<User>(), cancellationToken);

    Task<PlanReadModel?> FindActiveReadModelByUserIdAsync(Id<AccountReference> userId, CancellationToken cancellationToken = default)
        => FindActiveReadModelByUserIdAsync(userId.Rebind<User>(), cancellationToken);

    Task<Plan?> FindLastActiveByUserIdAsync(Id<AccountReference> userId, CancellationToken cancellationToken = default)
        => FindLastActiveByUserIdAsync(userId.Rebind<User>(), cancellationToken);

    Task<List<Plan>> GetByUserIdAsync(Id<AccountReference> userId, CancellationToken cancellationToken = default)
        => GetByUserIdAsync(userId.Rebind<User>(), cancellationToken);

    Task<List<PlanReadModel>> GetReadModelsByUserIdAsync(Id<AccountReference> userId, CancellationToken cancellationToken = default)
        => GetReadModelsByUserIdAsync(userId.Rebind<User>(), cancellationToken);

    Task SetActivePlanAsync(Id<AccountReference> userId, Id<PlanReference> planId, CancellationToken cancellationToken = default)
        => SetActivePlanAsync(userId.Rebind<User>(), planId.Rebind<Plan>(), cancellationToken);

    Task SetActivePlanAsync(Id<AccountReference> userId, Id<Plan> planId, CancellationToken cancellationToken = default)
        => SetActivePlanAsync(userId.Rebind<User>(), planId, cancellationToken);

    Task ClearActivePlansAsync(Id<AccountReference> userId, CancellationToken cancellationToken = default)
        => ClearActivePlansAsync(userId.Rebind<User>(), cancellationToken);

    Task<IReadOnlyCollection<Id<PlanExerciseReference>>> GetPlanExerciseIdsAsync(Id<PlanReference> planId, CancellationToken cancellationToken = default)
        => GetPlanExerciseIdsAsync(planId.Rebind<Plan>(), cancellationToken);

    Task<Plan> ClonePlanAsync(
        Id<PlanReference> sourcePlanId,
        Id<AccountReference> userId,
        IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>> exerciseIdMap,
        bool isActive = true,
        CancellationToken cancellationToken = default)
        => ClonePlanAsync(sourcePlanId.Rebind<Plan>(), userId.Rebind<User>(), exerciseIdMap, isActive, cancellationToken);

    Task<string> GenerateShareCodeAsync(Id<PlanReference> planId, Id<AccountReference> userId, CancellationToken cancellationToken = default)
        => GenerateShareCodeAsync(planId.Rebind<Plan>(), userId.Rebind<User>(), cancellationToken);
}

