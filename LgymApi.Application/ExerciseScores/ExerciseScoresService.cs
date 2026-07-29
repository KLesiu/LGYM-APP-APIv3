using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.ExerciseScores.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.ExerciseScores;

public sealed class ExerciseScoresService : IExerciseScoresService
{
    private readonly IWorkoutProgressReadWriteService _progress;

    public ExerciseScoresService(IWorkoutProgressReadWriteService progress)
    {
        _progress = progress;
    }

    public async Task<Result<List<ExerciseScoresChartData>, AppError>> GetExerciseScoresChartDataAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default)
    {
        var result = await _progress.GetExerciseScoreChartAsync(userId, exerciseId, cancellationToken);
        return result.IsFailure
            ? Result<List<ExerciseScoresChartData>, AppError>.Failure(result.Error)
            : Result<List<ExerciseScoresChartData>, AppError>.Success(result.Value.Select(point => new ExerciseScoresChartData
            {
                Id = point.Id,
                Value = point.Value,
                Date = point.Date,
                ExerciseName = point.ExerciseName,
                ExerciseId = point.ExerciseId
            }).ToList());
    }
}
