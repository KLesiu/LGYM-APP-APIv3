using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.TrainingPlanning.Plan.Models;

namespace LgymApi.Application.TrainingPlanning.Plan.CopyPlan;

public interface ICopyPlanUseCase
{
    Task<Result<PlanReadModel, AppError>> ExecuteAsync(CopyPlanCommand input, CancellationToken cancellationToken = default);
}
