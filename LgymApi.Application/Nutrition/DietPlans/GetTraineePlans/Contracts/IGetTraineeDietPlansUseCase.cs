using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.DietPlans.Models;

namespace LgymApi.Application.Nutrition.DietPlans.GetTraineePlans;

public interface IGetTraineeDietPlansUseCase
{
    Task<Result<IReadOnlyList<DietPlanReadModel>, AppError>> ExecuteAsync(
        GetTraineeDietPlansQuery query,
        CancellationToken cancellationToken = default);
}
