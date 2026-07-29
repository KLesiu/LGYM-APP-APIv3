using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.WorkoutProgress;

public sealed class WorkoutEloPersistenceRepository(AppDbContext dbContext) : IWorkoutEloPersistence
{
    public Task AddAsync(WorkoutEloWriteModel registry, CancellationToken cancellationToken = default)
        => dbContext.EloRegistries.AddAsync(ToEntity(registry), cancellationToken).AsTask();

    public Task CreateInitialForAccountAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => AddAsync(new WorkoutEloWriteModel(Id<EloRegistry>.New(), accountId, DateTimeOffset.UtcNow, 1000, null), cancellationToken);

    public Task<int?> GetLatestEloAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => dbContext.EloRegistries.AsNoTracking().Where(entry => entry.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId)).OrderByDescending(entry => entry.Date).Select(entry => (int?)entry.Elo).FirstOrDefaultAsync(cancellationToken);

    public async Task<WorkoutEloPersistenceModel?> GetLatestEntryAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.EloRegistries.AsNoTracking().Where(entry => entry.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId)).OrderByDescending(entry => entry.Date).FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : WorkoutPersistenceProjection.Elo(entity);
    }

    public async Task<IReadOnlyList<WorkoutEloPersistenceModel>> GetByAccountIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => (await dbContext.EloRegistries.AsNoTracking().Where(entry => entry.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId)).OrderBy(entry => entry.Date).ToListAsync(cancellationToken)).Select(WorkoutPersistenceProjection.Elo).ToList();

    private static EloRegistry ToEntity(WorkoutEloWriteModel model) => new() { Id = model.Id, UserId = WorkoutPersistenceAccountIds.ToPersisted(model.AccountId), Date = model.Date, Elo = model.Elo, TrainingId = model.TrainingId };
}
