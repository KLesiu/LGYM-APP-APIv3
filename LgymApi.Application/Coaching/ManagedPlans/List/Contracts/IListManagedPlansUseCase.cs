using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;

namespace LgymApi.Application.Coaching.ManagedPlans.List;

public interface IListManagedPlansUseCase
{
    Task<Result<IReadOnlyList<ManagedPlanReadModel>, AppError>> ExecuteAsync(
        ListManagedPlansQuery query,
        CancellationToken cancellationToken = default);
}
