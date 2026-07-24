using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Nutrition;

public sealed class DietPlanPersistenceRepository : IDietPlanPersistence
{
    private readonly AppDbContext _dbContext;

    public DietPlanPersistenceRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddPlanAsync(DietPlan plan, CancellationToken cancellationToken = default)
        => _dbContext.DietPlans.AddAsync(plan, cancellationToken).AsTask();

    public Task AddHistoryEntryAsync(DietPlanHistory historyEntry, CancellationToken cancellationToken = default)
        => _dbContext.DietPlanHistories.AddAsync(historyEntry, cancellationToken).AsTask();

    public Task<DietPlan?> FindTrackedPlanByIdAsync(Id<DietPlan> planId, CancellationToken cancellationToken = default)
        => _dbContext.DietPlans
            .Include(x => x.Meals.OrderBy(m => m.Order).ThenBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == planId, cancellationToken);

    public Task<DietPlan?> GetPlanByIdAsync(Id<DietPlan> planId, CancellationToken cancellationToken = default)
        => _dbContext.DietPlans
            .AsNoTracking()
            .Include(x => x.Meals.OrderBy(m => m.Order).ThenBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == planId, cancellationToken);

    public Task<List<DietPlan>> ListPlansByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default)
        => _dbContext.DietPlans
            .AsNoTracking()
            .Where(x => x.TrainerId == trainerId && x.TraineeId == traineeId && !x.IsDeleted)
            .Include(x => x.Meals.OrderBy(m => m.Order).ThenBy(m => m.CreatedAt))
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.StartDate)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<List<DietPlan>> ListActivePlansForTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default)
        => _dbContext.DietPlans
            .AsNoTracking()
            .Where(x => x.TraineeId == traineeId && x.IsActive && !x.IsDeleted)
            .Include(x => x.Meals.OrderBy(m => m.Order).ThenBy(m => m.CreatedAt))
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.StartDate)
            .ToListAsync(cancellationToken);

    public Task<DietPlan?> GetActivePlanForTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default)
        => _dbContext.DietPlans
            .AsNoTracking()
            .Where(x => x.TraineeId == traineeId && x.IsActive && !x.IsDeleted)
            .Include(x => x.Meals.OrderBy(m => m.Order).ThenBy(m => m.CreatedAt))
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.StartDate)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<DietPlanHistory>> ListPlanHistoryAsync(Id<DietPlan> planId, CancellationToken cancellationToken = default)
        => _dbContext.DietPlanHistories
            .AsNoTracking()
            .Where(x => x.DietPlanId == planId && !x.IsDeleted)
            .OrderByDescending(x => x.ChangeDate)
            .ToListAsync(cancellationToken);
}
