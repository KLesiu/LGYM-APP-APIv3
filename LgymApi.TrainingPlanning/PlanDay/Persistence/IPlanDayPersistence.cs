using LgymApi.Domain.ValueObjects;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.TrainingPlanning.PlanDay.Persistence;

internal interface IPlanDayPersistence
{
    Task<PlanDayPlanPersistenceModel?> FindPlanAsync(Id<PlanReference> planId, CancellationToken cancellationToken = default);
    Task<PlanDayPlanPersistenceModel?> FindActivePlanAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<PlanDayPersistenceModel?> FindPlanDayAsync(Id<PlanDayReference> planDayId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanDayPersistenceModel>> GetPlanDaysAsync(Id<PlanReference> planId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanDayPersistenceModel>> GetPlanDaysByIdsAsync(IReadOnlyCollection<Id<PlanDayReference>> planDayIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlanDayExercisePersistenceModel>> GetPlanDayExercisesAsync(IReadOnlyCollection<Id<PlanDayReference>> planDayIds, CancellationToken cancellationToken = default);
    Task CreatePlanDayAsync(Id<PlanReference> planId, PlanDayWriteModel input, CancellationToken cancellationToken = default);
    Task UpdatePlanDayAsync(Id<PlanDayReference> planDayId, string name, CancellationToken cancellationToken = default);
    Task ReplacePlanDayExercisesAsync(Id<PlanDayReference> planDayId, IReadOnlyList<PlanDayExerciseWriteModel> exercises, CancellationToken cancellationToken = default);
    Task MarkPlanDayDeletedAsync(Id<PlanDayReference> planDayId, CancellationToken cancellationToken = default);
}

internal sealed record PlanDayPlanPersistenceModel(
    Id<PlanReference> Id,
    Id<AccountReference> OwnerId);

internal sealed record PlanDayPersistenceModel(
    Id<PlanDayReference> Id,
    Id<PlanReference> PlanId,
    string Name,
    bool IsDeleted);

internal sealed record PlanDayExercisePersistenceModel(
    Id<PlanDayReference> PlanDayId,
    Id<PlanExerciseReference> ExerciseId,
    int Order,
    int Series,
    string Reps);
