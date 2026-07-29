using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Persistence;
using Microsoft.EntityFrameworkCore;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class ActivePlanPointerStore : IActivePlanPointerStore
{
    private readonly ITrainingPlanningPersistenceContext _context;

    public ActivePlanPointerStore(ITrainingPlanningPersistenceContext context)
    {
        _context = context;
    }

    public Task<Id<PlanEntity>?> GetActivePlanIdAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default)
        => _context.Plans
            .AsNoTracking()
            .Where(plan => plan.UserId == userId && plan.IsActive && !plan.IsDeleted)
            .OrderByDescending(plan => plan.CreatedAt)
            .Select(plan => (Id<PlanEntity>?)plan.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task StageActivePlanIdAsync(
        Id<UserEntity> userId,
        Id<PlanEntity>? planId,
        CancellationToken cancellationToken = default)
    {
        var persistedPlans = await _context.Plans
            .Where(plan => plan.UserId == userId && !plan.IsDeleted)
            .ToListAsync(cancellationToken);

        foreach (var plan in persistedPlans)
        {
            plan.IsActive = planId.HasValue && plan.Id == planId.Value;
        }

        foreach (var plan in _context.Plans.Local.Where(plan =>
                     plan.UserId == userId &&
                     !plan.IsDeleted &&
                     persistedPlans.All(persistedPlan => persistedPlan.Id != plan.Id)))
        {
            plan.IsActive = planId.HasValue && plan.Id == planId.Value;
        }
    }
}
