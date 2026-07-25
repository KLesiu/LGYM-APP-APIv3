using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Application.Nutrition.Supplementation.GetTraineePlans;

public interface IGetTraineeSupplementPlansUseCase
{
    Task<Result<IReadOnlyList<SupplementPlanReadModel>, AppError>> ExecuteAsync(
        GetTraineeSupplementPlansQuery query,
        CancellationToken cancellationToken = default);
}
