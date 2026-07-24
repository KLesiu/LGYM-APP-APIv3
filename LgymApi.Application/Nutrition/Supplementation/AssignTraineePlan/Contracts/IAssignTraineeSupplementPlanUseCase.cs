using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;

namespace LgymApi.Application.Nutrition.Supplementation.AssignTraineePlan;

public interface IAssignTraineeSupplementPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        AssignTraineeSupplementPlanCommand command,
        CancellationToken cancellationToken = default);
}
