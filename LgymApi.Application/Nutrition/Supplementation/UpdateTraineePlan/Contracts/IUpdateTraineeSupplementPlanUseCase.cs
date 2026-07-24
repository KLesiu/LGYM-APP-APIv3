using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Application.Nutrition.Supplementation.UpdateTraineePlan;

public interface IUpdateTraineeSupplementPlanUseCase
{
    Task<Result<SupplementPlanReadModel, AppError>> ExecuteAsync(
        UpdateTraineeSupplementPlanCommand command,
        CancellationToken cancellationToken = default);
}
