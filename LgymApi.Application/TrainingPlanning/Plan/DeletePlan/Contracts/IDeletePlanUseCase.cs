using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Plan.DeletePlan;

public interface IDeletePlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(DeletePlanCommand input, CancellationToken cancellationToken = default);
}
