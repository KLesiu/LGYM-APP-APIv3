using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Plan.SetActivePlan;

public interface ISetActivePlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(SetActivePlanCommand input, CancellationToken cancellationToken = default);
}
