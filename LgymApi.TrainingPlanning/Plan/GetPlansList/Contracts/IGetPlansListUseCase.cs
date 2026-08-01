using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.TrainingPlanning.Plan.Models;

namespace LgymApi.Application.TrainingPlanning.Plan.GetPlansList;

public interface IGetPlansListUseCase
{
    Task<Result<List<PlanReadModel>, AppError>> ExecuteAsync(GetPlansListQuery input, CancellationToken cancellationToken = default);
}
