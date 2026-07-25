using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Models;

namespace LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Contracts;

public interface IDeleteTraineeSupplementPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        DeleteTraineeSupplementPlanCommand command,
        CancellationToken cancellationToken = default);
}
