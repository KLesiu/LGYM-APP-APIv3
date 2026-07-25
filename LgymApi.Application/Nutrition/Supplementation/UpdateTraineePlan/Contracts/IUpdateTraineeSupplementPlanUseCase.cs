using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Application.Nutrition.Supplementation.UpdateTraineePlan;

public interface IUpdateTraineeSupplementPlanUseCase
{
    Task<Result<SupplementPlanReadModel, AppError>> ExecuteAsync(
        UpdateTraineeSupplementPlanCommand command,
        CancellationToken cancellationToken = default);
}
