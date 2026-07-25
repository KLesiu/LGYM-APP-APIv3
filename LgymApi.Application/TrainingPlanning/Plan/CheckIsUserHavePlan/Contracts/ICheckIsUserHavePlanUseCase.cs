using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Plan.CheckIsUserHavePlan;

/// <summary>Compatibility-only seam for the legacy check-is-user-have-plan endpoint.</summary>
public interface ICheckIsUserHavePlanUseCase
{
    Task<Result<bool, AppError>> ExecuteAsync(CheckIsUserHavePlanQuery input, CancellationToken cancellationToken = default);
}
