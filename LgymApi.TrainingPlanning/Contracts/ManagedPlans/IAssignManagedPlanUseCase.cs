using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public interface IAssignManagedPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        AssignManagedPlanCommand command,
        CancellationToken cancellationToken = default);
}
