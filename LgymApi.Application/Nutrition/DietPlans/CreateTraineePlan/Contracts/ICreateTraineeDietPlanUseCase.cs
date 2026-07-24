using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.DietPlans.Models;

namespace LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan;

public interface ICreateTraineeDietPlanUseCase
{
    Task<Result<DietPlanReadModel, AppError>> ExecuteAsync(
        CreateTraineeDietPlanCommand command,
        CancellationToken cancellationToken = default);
}
