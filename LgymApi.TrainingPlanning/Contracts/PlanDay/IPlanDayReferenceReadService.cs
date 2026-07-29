using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.TrainingPlanning.Contracts.PlanDay;

public interface IPlanDayReferenceReadService
{
    Task<PlanDayReferenceReadModel> GetByIdAsync(
        Id<PlanDayReference> planDayId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlanDayReferenceReadModel>> GetByIdsAsync(
        IReadOnlyList<Id<PlanDayReference>> planDayIds,
        CancellationToken cancellationToken = default);
}
