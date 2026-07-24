using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Models;

namespace LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Contracts;

public interface IActivateTraineeDietPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        ActivateTraineeDietPlanCommand command,
        CancellationToken cancellationToken = default);
}
