using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.DietPlans.Models;

namespace LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlans;

public interface IGetCurrentDietPlansUseCase
{
    Task<Result<IReadOnlyList<DietPlanReadModel>, AppError>> ExecuteAsync(
        GetCurrentDietPlansQuery query,
        CancellationToken cancellationToken = default);
}
