using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.TrainingPlanning.PlanDay.Persistence;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.TrainingPlanning.PlanDay;

internal sealed class PlanDayReferenceReadService(
    IPlanDayPersistence persistence,
    IMapper mapper) : IPlanDayReferenceReadService
{
    public async Task<PlanDayReferenceReadModel> GetByIdAsync(
        Id<PlanDayReference> planDayId,
        CancellationToken cancellationToken = default)
        => (await GetByIdsAsync([planDayId], cancellationToken))[0];

    public async Task<IReadOnlyList<PlanDayReferenceReadModel>> GetByIdsAsync(
        IReadOnlyList<Id<PlanDayReference>> planDayIds,
        CancellationToken cancellationToken = default)
    {
        var planDays = await persistence.GetPlanDaysByIdsAsync(planDayIds, cancellationToken);
        var planDaysById = mapper
            .MapList<PlanDayPersistenceModel, PlanDayReferenceReadModel>(planDays)
            .ToDictionary(planDay => planDay.PlanDayId);

        return planDayIds
            .Select(planDayId => planDaysById.GetValueOrDefault(
                planDayId,
                new PlanDayReferenceReadModel(planDayId, Id<PlanReference>.Empty, string.Empty, false, false)))
            .ToList();
    }
}
