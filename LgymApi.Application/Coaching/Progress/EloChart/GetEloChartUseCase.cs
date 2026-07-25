using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.Dashboard;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;

namespace LgymApi.Application.Coaching.Progress.EloChart;

internal sealed class GetEloChartUseCase : IGetEloChartUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly IWorkoutProgressDashboardReadService _progress;

    public GetEloChartUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        IWorkoutProgressDashboardReadService progress)
    {
        _relationshipAccess = relationshipAccess;
        _progress = progress;
    }

    public async Task<Result<List<EloChartPoint>, AppError>> ExecuteAsync(
        GetEloChartQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = ProgressReadAccess.GetError(access, query.TraineeId);
        if (accessError is not null)
        {
            return Result<List<EloChartPoint>, AppError>.Failure(accessError);
        }

        var result = await _progress.GetEloChartAsync(query.TraineeId, cancellationToken);
        return result.IsFailure
            ? Result<List<EloChartPoint>, AppError>.Failure(new TrainerRelationshipNotFoundError(result.Error.Message))
            : Result<List<EloChartPoint>, AppError>.Success(result.Value);
    }
}
