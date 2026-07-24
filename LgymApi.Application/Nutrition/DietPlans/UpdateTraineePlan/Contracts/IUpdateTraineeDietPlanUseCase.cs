using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.DietPlans.Models;

namespace LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan.Contracts;

public interface IUpdateTraineeDietPlanUseCase
{
    Task<Result<DietPlanReadModel, AppError>> ExecuteAsync(
        UpdateTraineeDietPlanCommand command,
        CancellationToken cancellationToken = default);
}
