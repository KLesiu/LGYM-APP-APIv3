using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.DietPlans.Models;

namespace LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan.Contracts;

public interface IUpdateTraineeDietPlanUseCase
{
    Task<Result<DietPlanReadModel, AppError>> ExecuteAsync(
        UpdateTraineeDietPlanCommand command,
        CancellationToken cancellationToken = default);
}
