using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.Dashboard;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.Progress.ExerciseScoresChart;

internal sealed class GetExerciseScoresChartUseCase : IGetExerciseScoresChartUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly IWorkoutProgressDashboardReadService _progress;

    public GetExerciseScoresChartUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        IWorkoutProgressDashboardReadService progress)
    {
        _relationshipAccess = relationshipAccess;
        _progress = progress;
    }

    public async Task<Result<List<ExerciseScoreChartPoint>, AppError>> ExecuteAsync(
        GetExerciseScoresChartQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = ProgressReadAccess.GetError(access, query.TraineeId);
        if (accessError is not null)
        {
            return Result<List<ExerciseScoreChartPoint>, AppError>.Failure(accessError);
        }

        if (query.ExerciseId.IsEmpty)
        {
            return Result<List<ExerciseScoreChartPoint>, AppError>.Failure(
                new InvalidTrainerRelationshipError(Messages.ExerciseIdRequired));
        }

        var result = await _progress.GetExerciseScoreChartAsync(
            query.TraineeId,
            query.ExerciseId.ToString(),
            cancellationToken);
        return result.IsFailure
            ? Result<List<ExerciseScoreChartPoint>, AppError>.Failure(
                new TrainerRelationshipNotFoundError(result.Error.Message))
            : Result<List<ExerciseScoreChartPoint>, AppError>.Success(result.Value);
    }
}
