using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;
using LgymApi.Application.WorkoutProgress.Persistence;

namespace LgymApi.Application.WorkoutProgress.Adapters;

internal sealed class PlanExerciseCatalogAdapter(
    IWorkoutExercisePersistence exercises,
    IMapper mapper) : IPlanExerciseCatalogPort
{
    public async Task<IReadOnlyDictionary<Id<PlanExerciseReference>, PlanExerciseCatalogItem>> GetByIdsAsync(
        IReadOnlyCollection<Id<PlanExerciseReference>> exerciseIds,
        IReadOnlyList<string> cultures,
        CancellationToken cancellationToken = default)
    {
        var requestedIds = exerciseIds.Distinct().ToArray();
        if (requestedIds.Length == 0)
        {
            return new Dictionary<Id<PlanExerciseReference>, PlanExerciseCatalogItem>();
        }

        var exercisesById = (await exercises.GetByIdsAsync(
                requestedIds.Select(id => id.Rebind<Exercise>()).ToList(),
                cancellationToken))
            .ToDictionary(exercise => exercise.Id);
        var globalExerciseIds = exercisesById.Values
            .Where(exercise => exercise.OwnerId is null)
            .Select(exercise => exercise.Id)
            .ToArray();
        var translations = await exercises.GetTranslationsAsync(globalExerciseIds, cultures, cancellationToken);

        return requestedIds
            .Where(id => exercisesById.ContainsKey(id.Rebind<Exercise>()))
            .Select(id =>
            {
                var exercise = exercisesById[id.Rebind<Exercise>()];
                var item = mapper.Map<WorkoutExercisePersistenceModel, PlanExerciseCatalogItem>(exercise);
                return exercise.OwnerId is null && translations.TryGetValue(exercise.Id, out var translation)
                    ? item with { Name = translation }
                    : item;
            })
            .ToDictionary(item => item.Id);
    }
}
