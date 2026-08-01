using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.Dashboard;

namespace LgymApi.Application.Coaching.Progress.TrainingDates;

internal sealed class GetTrainingDatesUseCase : IGetTrainingDatesUseCase
{
    private readonly IMarkerCoachingRelationshipAccessService _relationshipAccess;
    private readonly IWorkoutProgressDashboardReadService _progress;

    public GetTrainingDatesUseCase(
        IMarkerCoachingRelationshipAccessService relationshipAccess,
        IWorkoutProgressDashboardReadService progress)
    {
        _relationshipAccess = relationshipAccess;
        _progress = progress;
    }

    public async Task<Result<List<DateTime>, AppError>> ExecuteAsync(
        GetTrainingDatesQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = ProgressReadAccess.GetError(access, query.TraineeId);
        if (accessError is not null)
        {
            return Result<List<DateTime>, AppError>.Failure(accessError);
        }

        var result = await _progress.GetTrainingDatesAsync(query.TraineeId, cancellationToken);
        return result.IsFailure
            ? Result<List<DateTime>, AppError>.Failure(new TrainerRelationshipNotFoundError(result.Error.Message))
            : Result<List<DateTime>, AppError>.Success(result.Value);
    }
}
