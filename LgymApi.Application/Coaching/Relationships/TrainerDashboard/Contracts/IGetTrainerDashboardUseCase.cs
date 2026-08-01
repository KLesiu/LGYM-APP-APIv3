using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Pagination;

namespace LgymApi.Application.Coaching.Relationships.TrainerDashboard;

public interface IGetTrainerDashboardUseCase
{
    Task<Result<Pagination<TrainerDashboardTraineeReadModel>, AppError>> ExecuteAsync(
        GetTrainerDashboardQuery query,
        CancellationToken cancellationToken = default);
}
