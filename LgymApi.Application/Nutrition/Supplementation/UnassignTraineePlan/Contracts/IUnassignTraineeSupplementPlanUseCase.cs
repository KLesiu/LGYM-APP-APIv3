using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan.Contracts;

public interface IUnassignTraineeSupplementPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        UnassignTraineeSupplementPlanCommand command,
        CancellationToken cancellationToken = default);
}
