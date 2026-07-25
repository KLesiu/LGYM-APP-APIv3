using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.Dashboard;
using LgymApi.Application.WorkoutProgress.Dashboard.Models;

namespace LgymApi.Application.Coaching.Progress.TrainingByDate;

internal sealed class GetTrainingByDateUseCase : IGetTrainingByDateUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly IWorkoutProgressDashboardReadService _progress;

    public GetTrainingByDateUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        IWorkoutProgressDashboardReadService progress)
    {
        _relationshipAccess = relationshipAccess;
        _progress = progress;
    }

    public async Task<Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>> ExecuteAsync(
        GetTrainingByDateQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = ProgressReadAccess.GetError(access, query.TraineeId);
        if (accessError is not null)
        {
            return Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>.Failure(accessError);
        }

        var result = await _progress.GetTrainingByDateAsync(
            query.TraineeId,
            query.CreatedAt,
            cancellationToken);
        return result.IsFailure
            ? Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>.Failure(
                new TrainerRelationshipNotFoundError(result.Error.Message))
            : Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>.Success(result.Value);
    }
}
