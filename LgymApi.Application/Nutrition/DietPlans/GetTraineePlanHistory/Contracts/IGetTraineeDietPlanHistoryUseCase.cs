using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Models;
using LgymApi.Application.Nutrition.DietPlans.Models;

namespace LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Contracts;

public interface IGetTraineeDietPlanHistoryUseCase
{
    Task<Result<IReadOnlyList<DietPlanHistoryReadModel>, AppError>> ExecuteAsync(
        GetTraineeDietPlanHistoryQuery query,
        CancellationToken cancellationToken = default);
}
