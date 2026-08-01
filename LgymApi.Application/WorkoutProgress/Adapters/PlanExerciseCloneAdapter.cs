using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using LgymApi.Application.WorkoutProgress.Persistence;

namespace LgymApi.Application.WorkoutProgress.Adapters;

internal sealed class PlanExerciseCloneAdapter(IWorkoutExercisePersistence exercises) : IPlanExerciseClonePort
{
    public async Task<IReadOnlyDictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>>> StageClonesAsync(
        Id<AccountReference> targetAccountId,
        IReadOnlyCollection<Id<PlanExerciseReference>> exerciseIds,
        CancellationToken cancellationToken = default)
    {
        var requestedIds = exerciseIds.Distinct().ToArray();
        if (requestedIds.Length == 0)
        {
            return new Dictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>>();
        }

        var sourceExercises = (await exercises.GetByIdsAsync(
                requestedIds.Select(id => id.Rebind<Exercise>()).ToList(),
                cancellationToken))
            .ToDictionary(exercise => exercise.Id);
        var clonedIds = new Dictionary<Id<PlanExerciseReference>, Id<PlanExerciseReference>>();

        foreach (var sourceId in requestedIds)
        {
            if (!sourceExercises.TryGetValue(sourceId.Rebind<Exercise>(), out var sourceExercise))
            {
                continue;
            }

            if (sourceExercise.OwnerId is null)
            {
                clonedIds[sourceId] = sourceId;
                continue;
            }

            var clonedExercise = new WorkoutExerciseWriteModel(
                Id<Exercise>.New(),
                targetAccountId,
                sourceExercise.Name,
                sourceExercise.BodyPart,
                ExerciseEloFormula.Standard,
                sourceExercise.Description,
                sourceExercise.Image,
                false);
            await exercises.AddAsync(clonedExercise, cancellationToken);
            clonedIds[sourceId] = clonedExercise.Id.Rebind<PlanExerciseReference>();
        }

        return clonedIds;
    }
}
