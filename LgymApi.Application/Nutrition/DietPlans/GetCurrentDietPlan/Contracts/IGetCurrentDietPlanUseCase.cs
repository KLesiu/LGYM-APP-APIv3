using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.DietPlans.Models;

namespace LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlan;

public interface IGetCurrentDietPlanUseCase
{
    Task<Result<DietPlanReadModel, AppError>> ExecuteAsync(
        GetCurrentDietPlanQuery query,
        CancellationToken cancellationToken = default);
}
