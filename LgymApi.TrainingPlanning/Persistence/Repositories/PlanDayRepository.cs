using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class PlanDayRepository : IPlanDayRepository
{
    private readonly ITrainingPlanningPersistenceContext _context;

    public PlanDayRepository(ITrainingPlanningPersistenceContext context)
    {
        _context = context;
    }

    public Task<PlanDay?> FindByIdAsync(Id<PlanDay> id, CancellationToken cancellationToken = default)
        => _context.PlanDays.FirstOrDefaultAsync(planDay => planDay.Id == id, cancellationToken);

    public Task<List<PlanDay>> GetByPlanIdAsync(Id<Plan> planId, CancellationToken cancellationToken = default)
        => _context.PlanDays.AsNoTracking().Where(planDay => planDay.PlanId == planId && !planDay.IsDeleted).ToListAsync(cancellationToken);

    public Task<List<PlanDay>> GetByIdsAsync(IReadOnlyCollection<Id<PlanDay>> ids, CancellationToken cancellationToken = default)
        => _context.PlanDays.AsNoTracking().Where(planDay => ids.Contains(planDay.Id)).ToListAsync(cancellationToken);

    public Task AddAsync(PlanDay planDay, CancellationToken cancellationToken = default)
        => _context.PlanDays.AddAsync(planDay, cancellationToken).AsTask();

    public Task UpdateAsync(PlanDay planDay, CancellationToken cancellationToken = default)
    {
        _context.PlanDays.Update(planDay);
        return Task.CompletedTask;
    }

    public async Task MarkDeletedAsync(Id<PlanDay> planDayId, CancellationToken cancellationToken = default)
    {
        var planDays = await _context.PlanDays.Where(planDay => planDay.Id == planDayId).ToListAsync(cancellationToken);
        foreach (var planDay in planDays)
        {
            planDay.IsDeleted = true;
        }
    }

    public async Task MarkDeletedByPlanIdAsync(Id<Plan> planId, CancellationToken cancellationToken = default)
    {
        var planDays = await _context.PlanDays.Where(planDay => planDay.PlanId == planId).ToListAsync(cancellationToken);
        foreach (var planDay in planDays)
        {
            planDay.IsDeleted = true;
        }
    }

    public Task<bool> AnyByPlanIdAsync(Id<Plan> planId, CancellationToken cancellationToken = default)
        => _context.PlanDays.AnyAsync(planDay => planDay.PlanId == planId && !planDay.IsDeleted, cancellationToken);
}
