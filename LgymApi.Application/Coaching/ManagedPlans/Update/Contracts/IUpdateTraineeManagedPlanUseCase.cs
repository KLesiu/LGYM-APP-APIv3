using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

namespace LgymApi.Application.Coaching.ManagedPlans.Update;

public interface IUpdateTraineeManagedPlanUseCase
{
    Task<Result<ManagedPlanReadModel, AppError>> ExecuteAsync(
        UpdateTraineeManagedPlanCommand command,
        CancellationToken cancellationToken = default);
}
