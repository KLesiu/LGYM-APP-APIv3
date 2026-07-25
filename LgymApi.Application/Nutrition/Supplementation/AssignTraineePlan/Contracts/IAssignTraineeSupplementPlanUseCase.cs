using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Nutrition.Supplementation.AssignTraineePlan;

public interface IAssignTraineeSupplementPlanUseCase
{
    Task<Result<Unit, AppError>> ExecuteAsync(
        AssignTraineeSupplementPlanCommand command,
        CancellationToken cancellationToken = default);
}
