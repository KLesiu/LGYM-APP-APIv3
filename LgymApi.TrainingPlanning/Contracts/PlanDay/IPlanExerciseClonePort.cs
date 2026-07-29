using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.TrainingPlanning.Contracts.PlanDay;

public interface IPlanExerciseClonePort
{
    Task<IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>>> StageClonesAsync(
        Id<AccountReference> targetAccountId,
        IReadOnlyCollection<Id<PlanExerciseReference>> exerciseIds,
        CancellationToken cancellationToken = default);
}
