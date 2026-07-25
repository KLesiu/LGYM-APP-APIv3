using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.ManagedPlans.Assign;

public interface IAssignTraineeManagedPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        AssignTraineeManagedPlanCommand command,
        CancellationToken cancellationToken = default);
}
