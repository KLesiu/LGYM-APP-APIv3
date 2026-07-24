using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.DietPlans.Models;

namespace LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlans;

public interface IGetCurrentDietPlansUseCase
{
    Task<Result<IReadOnlyList<DietPlanReadModel>, AppError>> ExecuteAsync(
        GetCurrentDietPlansQuery query,
        CancellationToken cancellationToken = default);
}
