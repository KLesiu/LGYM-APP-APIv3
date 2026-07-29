using LgymApi.Application.WorkoutProgress.ProgressData.Models;

namespace LgymApi.Application.Features.Exercise.Models;

public sealed class SeriesScoreResult
{
    public int Series { get; init; }
    public WorkoutExerciseScoreReadModel? Score { get; init; }
}
