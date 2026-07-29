using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

public interface IGetManagedPlansUseCase
{
    Task<Result<IReadOnlyList<ManagedPlanReadModel>, AppError>> ExecuteAsync(
        GetManagedPlansQuery query,
        CancellationToken cancellationToken = default);
}
