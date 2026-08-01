using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Nutrition;

public sealed class SupplementationPersistenceRepository : ISupplementationPersistence
{
    private readonly AppDbContext _dbContext;

    public SupplementationPersistenceRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddPlanAsync(SupplementPlan plan, CancellationToken cancellationToken = default)
        => _dbContext.SupplementPlans.AddAsync(plan, cancellationToken).AsTask();

    public Task<SupplementPlan?> FindTrackedPlanByIdAsync(Id<SupplementPlan> planId, CancellationToken cancellationToken = default)
        => _dbContext.SupplementPlans
            .Include(x => x.Items.OrderBy(i => i.Order).ThenBy(i => i.TimeOfDay).ThenBy(i => i.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == planId, cancellationToken);

    public Task<List<SupplementPlan>> ListTrackedPlansByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default)
        => _dbContext.SupplementPlans
            .Where(x => x.TrainerId == trainerId && x.TraineeId == traineeId && !x.IsDeleted)
            .Include(x => x.Items.OrderBy(i => i.Order).ThenBy(i => i.TimeOfDay).ThenBy(i => i.CreatedAt))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<SupplementPlan?> GetTrackedActivePlanForTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default)
        => _dbContext.SupplementPlans
            .Where(x => x.TraineeId == traineeId && x.IsActive && !x.IsDeleted)
            .Include(x => x.Items.OrderBy(i => i.Order).ThenBy(i => i.TimeOfDay).ThenBy(i => i.CreatedAt))
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<SupplementIntakeLog?> FindTrackedIntakeLogAsync(Id<User> traineeId, Id<SupplementPlanItem> planItemId, DateOnly intakeDate, CancellationToken cancellationToken = default)
        => _dbContext.SupplementIntakeLogs
            .FirstOrDefaultAsync(x => x.TraineeId == traineeId && x.PlanItemId == planItemId && x.IntakeDate == intakeDate, cancellationToken);

    public Task<List<SupplementPlan>> ListPlansByTrainerAndTraineeAsync(Id<User> trainerId, Id<User> traineeId, CancellationToken cancellationToken = default)
        => _dbContext.SupplementPlans
            .AsNoTracking()
            .Where(x => x.TrainerId == trainerId && x.TraineeId == traineeId && !x.IsDeleted)
            .Include(x => x.Items.OrderBy(i => i.Order).ThenBy(i => i.TimeOfDay).ThenBy(i => i.CreatedAt))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<SupplementPlan?> GetActivePlanForTraineeAsync(Id<User> traineeId, CancellationToken cancellationToken = default)
        => _dbContext.SupplementPlans
            .AsNoTracking()
            .Where(x => x.TraineeId == traineeId && x.IsActive && !x.IsDeleted)
            .Include(x => x.Items.OrderBy(i => i.Order).ThenBy(i => i.TimeOfDay).ThenBy(i => i.CreatedAt))
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<List<SupplementIntakeLog>> ListIntakeLogsForPlanAsync(Id<User> traineeId, Id<SupplementPlan> planId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
        => _dbContext.SupplementIntakeLogs
            .AsNoTracking()
            .Where(x => x.TraineeId == traineeId
                        && x.PlanItem.PlanId == planId
                        && x.IntakeDate >= fromDate
                        && x.IntakeDate <= toDate)
            .Include(x => x.PlanItem)
            .OrderBy(x => x.IntakeDate)
            .ThenBy(x => x.TakenAt)
            .ToListAsync(cancellationToken);

    public Task<SupplementIntakeLog?> FindIntakeLogAsync(Id<User> traineeId, Id<SupplementPlanItem> planItemId, DateOnly intakeDate, CancellationToken cancellationToken = default)
        => _dbContext.SupplementIntakeLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TraineeId == traineeId && x.PlanItemId == planItemId && x.IntakeDate == intakeDate, cancellationToken);

    public Task AddIntakeLogAsync(SupplementIntakeLog intakeLog, CancellationToken cancellationToken = default)
        => _dbContext.SupplementIntakeLogs.AddAsync(intakeLog, cancellationToken).AsTask();

    public void DetachIntakeLog(SupplementIntakeLog intakeLog)
    {
        _dbContext.Entry(intakeLog).State = EntityState.Detached;
    }
}
