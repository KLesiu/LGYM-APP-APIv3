using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.ManagedPlans.Unassign;

public interface IUnassignTraineeManagedPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        UnassignTraineeManagedPlanCommand command,
        CancellationToken cancellationToken = default);
}
