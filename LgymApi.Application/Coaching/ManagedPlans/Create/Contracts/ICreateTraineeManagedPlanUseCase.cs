using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

namespace LgymApi.Application.Coaching.ManagedPlans.Create;

public interface ICreateTraineeManagedPlanUseCase
{
    Task<Result<ManagedPlanReadModel, AppError>> ExecuteAsync(
        CreateTraineeManagedPlanCommand command,
        CancellationToken cancellationToken = default);
}
