using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public interface IGetActiveAssignedPlanUseCase
{
    Task<Result<ManagedPlanReadModel, AppError>> ExecuteAsync(
        GetActiveAssignedPlanQuery query,
        CancellationToken cancellationToken = default);
}
