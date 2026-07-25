using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;

namespace LgymApi.Application.Coaching.Progress.ExerciseScoresChart;

public interface IGetExerciseScoresChartUseCase
{
    Task<Result<List<ExerciseScoreChartPoint>, AppError>> ExecuteAsync(
        GetExerciseScoresChartQuery query,
        CancellationToken cancellationToken = default);
}
