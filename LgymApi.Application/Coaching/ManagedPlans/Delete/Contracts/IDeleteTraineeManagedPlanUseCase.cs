using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.ManagedPlans.Delete;

public interface IDeleteTraineeManagedPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        DeleteTraineeManagedPlanCommand command,
        CancellationToken cancellationToken = default);
}
