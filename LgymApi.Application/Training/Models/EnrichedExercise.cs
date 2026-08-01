using LgymApi.Application.WorkoutProgress.ProgressData.Models;

namespace LgymApi.Application.Features.Training.Models;

public sealed class EnrichedExercise
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ExerciseScore> ExerciseScoreId { get; init; }
    public ProgressExerciseReadModel ExerciseDetails { get; init; } = null!;
    public List<WorkoutExerciseScoreReadModel> ScoresDetails { get; init; } = new();
}
