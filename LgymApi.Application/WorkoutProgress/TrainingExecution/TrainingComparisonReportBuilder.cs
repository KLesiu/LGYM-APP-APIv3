using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

public static class TrainingComparisonReportBuilder
{
    public static List<GroupedExerciseComparison> Build(
        IReadOnlyCollection<TrainingExerciseInput> currentExercises,
        Dictionary<string, WorkoutExerciseScorePersistenceModel> previousScores,
        Dictionary<Id<Exercise>, string> exerciseDetails)
    {
        var comparisonMap = new Dictionary<Id<Exercise>, GroupedExerciseComparison>();

        foreach (var current in currentExercises)
        {
            if (current.ExerciseId.IsEmpty)
            {
                continue;
            }

            var exerciseId = current.ExerciseId;
            if (!comparisonMap.TryGetValue(exerciseId, out var group))
            {
                comparisonMap[exerciseId] = new GroupedExerciseComparison
                {
                    ExerciseId = exerciseId,
                    ExerciseName = exerciseDetails.TryGetValue(exerciseId, out var name) ? name : "Nieznane cwiczenie",
                    SeriesComparisons = new List<SeriesComparison>()
                };
            }

            var key = $"{exerciseId}-{current.Series}";
            previousScores.TryGetValue(key, out var previous);
            comparisonMap[exerciseId].SeriesComparisons.Add(new SeriesComparison
            {
                Series = current.Series,
                CurrentResult = new ScoreResult
                {
                    Reps = current.Reps,
                    Weight = current.Weight,
                    Unit = current.Unit
                },
                PreviousResult = previous == null
                    ? null
                    : new ScoreResult
                    {
                        Reps = previous.Reps,
                        Weight = previous.Weight,
                        Unit = previous.Unit
                    }
            });
        }

        return currentExercises
            .Select(exercise => exercise.ExerciseId)
            .Where(exerciseId => !exerciseId.IsEmpty)
            .Distinct()
            .Where(comparisonMap.ContainsKey)
            .Select(exerciseId => comparisonMap[exerciseId])
            .ToList();
    }
}
