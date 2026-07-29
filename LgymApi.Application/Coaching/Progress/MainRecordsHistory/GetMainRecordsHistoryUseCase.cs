using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.Dashboard;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;

namespace LgymApi.Application.Coaching.Progress.MainRecordsHistory;

internal sealed class GetMainRecordsHistoryUseCase : IGetMainRecordsHistoryUseCase
{
    private readonly IMarkerCoachingRelationshipAccessService _relationshipAccess;
    private readonly IWorkoutProgressDashboardReadService _progress;

    public GetMainRecordsHistoryUseCase(
        IMarkerCoachingRelationshipAccessService relationshipAccess,
        IWorkoutProgressDashboardReadService progress)
    {
        _relationshipAccess = relationshipAccess;
        _progress = progress;
    }

    public async Task<Result<List<MainRecordReadModel>, AppError>> ExecuteAsync(
        GetMainRecordsHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = ProgressReadAccess.GetError(access, query.TraineeId);
        if (accessError is not null)
        {
            return Result<List<MainRecordReadModel>, AppError>.Failure(accessError);
        }

        var result = await _progress.GetMainRecordHistoryAsync(query.TraineeId, cancellationToken);
        return result.IsFailure
            ? Result<List<MainRecordReadModel>, AppError>.Failure(new TrainerRelationshipNotFoundError(result.Error.Message))
            : Result<List<MainRecordReadModel>, AppError>.Success(result.Value);
    }
}
