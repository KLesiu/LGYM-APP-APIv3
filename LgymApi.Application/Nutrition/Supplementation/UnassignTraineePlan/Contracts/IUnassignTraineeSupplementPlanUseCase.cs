using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;

namespace LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan.Contracts;

public interface IUnassignTraineeSupplementPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        UnassignTraineeSupplementPlanCommand command,
        CancellationToken cancellationToken = default);
}
