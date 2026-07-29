using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Plan.CreatePlan;

public interface ICreatePlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(CreatePlanCommand input, CancellationToken cancellationToken = default);
}
