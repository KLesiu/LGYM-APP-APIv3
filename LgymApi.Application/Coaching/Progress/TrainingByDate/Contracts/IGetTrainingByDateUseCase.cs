using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.Dashboard.Models;

namespace LgymApi.Application.Coaching.Progress.TrainingByDate;

public interface IGetTrainingByDateUseCase
{
    Task<Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>> ExecuteAsync(
        GetTrainingByDateQuery query,
        CancellationToken cancellationToken = default);
}
