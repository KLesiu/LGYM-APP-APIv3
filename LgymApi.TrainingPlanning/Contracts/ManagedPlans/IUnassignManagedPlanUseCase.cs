using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public interface IUnassignManagedPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        UnassignManagedPlanCommand command,
        CancellationToken cancellationToken = default);
}
