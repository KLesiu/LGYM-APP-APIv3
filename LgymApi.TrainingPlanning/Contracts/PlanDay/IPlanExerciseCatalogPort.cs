using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.TrainingPlanning.Contracts.PlanDay;

public interface IPlanExerciseCatalogPort
{
    Task<IReadOnlyDictionary<Id<PlanExerciseReference>, PlanExerciseCatalogItem>> GetByIdsAsync(
        IReadOnlyCollection<Id<PlanExerciseReference>> exerciseIds,
        IReadOnlyList<string> cultures,
        CancellationToken cancellationToken = default);
}

public sealed record PlanExerciseCatalogItem(
    Id<PlanExerciseReference> Id,
    string Name,
    Id<AccountReference>? OwnerId,
    BodyParts BodyPart,
    ExerciseEloFormula EloFormula,
    string? Description,
    string? Image);
