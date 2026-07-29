using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Repositories;

internal interface IPlanDayRepository
{
    Task<PlanDay?> FindByIdAsync(Id<PlanDay> id, CancellationToken cancellationToken = default);
    Task<List<PlanDay>> GetByPlanIdAsync(Id<Plan> planId, CancellationToken cancellationToken = default);
    Task<List<PlanDay>> GetByIdsAsync(IReadOnlyCollection<Id<PlanDay>> ids, CancellationToken cancellationToken = default);
    Task AddAsync(PlanDay planDay, CancellationToken cancellationToken = default);
    Task UpdateAsync(PlanDay planDay, CancellationToken cancellationToken = default);
    Task MarkDeletedAsync(Id<PlanDay> planDayId, CancellationToken cancellationToken = default);
    Task MarkDeletedByPlanIdAsync(Id<Plan> planId, CancellationToken cancellationToken = default);
    Task<bool> AnyByPlanIdAsync(Id<Plan> planId, CancellationToken cancellationToken = default);
}
