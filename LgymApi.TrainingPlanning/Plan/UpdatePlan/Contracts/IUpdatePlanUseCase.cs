using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Plan.UpdatePlan;

public interface IUpdatePlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(UpdatePlanCommand input, CancellationToken cancellationToken = default);
}
