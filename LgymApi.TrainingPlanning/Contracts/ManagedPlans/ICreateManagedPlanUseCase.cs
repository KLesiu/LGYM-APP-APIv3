using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public interface ICreateManagedPlanUseCase
{
    Task<Result<ManagedPlanReadModel, AppError>> ExecuteAsync(
        CreateManagedPlanCommand command,
        CancellationToken cancellationToken = default);
}
