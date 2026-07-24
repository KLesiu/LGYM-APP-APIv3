using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Models;

namespace LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Contracts;

public interface IDeleteTraineeSupplementPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        DeleteTraineeSupplementPlanCommand command,
        CancellationToken cancellationToken = default);
}
