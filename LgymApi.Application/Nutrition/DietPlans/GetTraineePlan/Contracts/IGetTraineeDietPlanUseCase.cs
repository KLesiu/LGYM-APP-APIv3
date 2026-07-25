using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.DietPlans.Models;

namespace LgymApi.Application.Nutrition.DietPlans.GetTraineePlan;

public interface IGetTraineeDietPlanUseCase
{
    Task<Result<DietPlanReadModel, AppError>> ExecuteAsync(
        GetTraineeDietPlanQuery query,
        CancellationToken cancellationToken = default);
}
