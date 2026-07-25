using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Models;

namespace LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Contracts;

public interface IDeleteTraineeDietPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        DeleteTraineeDietPlanCommand command,
        CancellationToken cancellationToken = default);
}
