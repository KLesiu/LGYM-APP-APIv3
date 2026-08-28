using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.DietPlans.Models;

namespace LgymApi.Application.Nutrition.DietPlans.GetOwnPlanHistory;

internal interface IGetOwnDietPlanHistoryUseCase
{
    Task<Result<IReadOnlyList<DietPlanHistoryReadModel>, AppError>> ExecuteAsync(
        GetOwnDietPlanHistoryQuery query,
        CancellationToken cancellationToken = default);
}
