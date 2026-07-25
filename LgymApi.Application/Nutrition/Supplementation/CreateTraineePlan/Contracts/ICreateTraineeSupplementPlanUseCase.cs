using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Application.Nutrition.Supplementation.CreateTraineePlan;

public interface ICreateTraineeSupplementPlanUseCase
{
    Task<Result<SupplementPlanReadModel, AppError>> ExecuteAsync(
        CreateTraineeSupplementPlanCommand command,
        CancellationToken cancellationToken = default);
}
