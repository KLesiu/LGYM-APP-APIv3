using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public interface IDeleteManagedPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        DeleteManagedPlanCommand command,
        CancellationToken cancellationToken = default);
}
