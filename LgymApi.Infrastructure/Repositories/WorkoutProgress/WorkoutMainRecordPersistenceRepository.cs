using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.WorkoutProgress;

public sealed class WorkoutMainRecordPersistenceRepository(AppDbContext dbContext) : IWorkoutMainRecordPersistence
{
    public Task AddAsync(WorkoutMainRecordWriteModel record, CancellationToken cancellationToken = default)
        => dbContext.MainRecords.AddAsync(ToEntity(record), cancellationToken).AsTask();

    public Task<IReadOnlyList<WorkoutMainRecordPersistenceModel>> GetByAccountIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => ReadAsync(dbContext.MainRecords.Where(record => record.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId)), cancellationToken);

    public Task<IReadOnlyList<WorkoutMainRecordPersistenceModel>> GetBestByAccountGroupedByExerciseAndUnitAsync(Id<AccountReference> accountId, IReadOnlyCollection<Id<Exercise>>? exerciseIds = null, CancellationToken cancellationToken = default)
    {
        var query = dbContext.MainRecords.Where(record => record.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId));
        if (exerciseIds is { Count: > 0 }) query = query.Where(record => exerciseIds.Contains(record.ExerciseId));
        return ReadAsync(query.GroupBy(record => new { record.ExerciseId, record.Unit }).Select(group => group.OrderByDescending(record => record.WeightValue).ThenByDescending(record => record.Date).First()), cancellationToken);
    }

    public async Task<WorkoutMainRecordPersistenceModel?> FindByIdAsync(Id<MainRecord> id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.MainRecords.AsNoTracking().FirstOrDefaultAsync(record => record.Id == id, cancellationToken);
        return entity is null ? null : WorkoutPersistenceProjection.MainRecord(entity);
    }

    public async Task DeleteAsync(Id<MainRecord> id, CancellationToken cancellationToken = default)
        => (await dbContext.MainRecords.FirstAsync(record => record.Id == id, cancellationToken)).IsDeleted = true;

    public async Task UpdateAsync(WorkoutMainRecordWriteModel record, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.MainRecords.FirstAsync(item => item.Id == record.Id, cancellationToken);
        entity.UserId = WorkoutPersistenceAccountIds.ToPersisted(record.AccountId);
        entity.ExerciseId = record.ExerciseId;
        entity.Weight = new Weight(record.Weight, record.Unit);
        entity.Date = record.Date;
    }

    private static MainRecord ToEntity(WorkoutMainRecordWriteModel model) => new() { Id = model.Id, UserId = WorkoutPersistenceAccountIds.ToPersisted(model.AccountId), ExerciseId = model.ExerciseId, Weight = new Weight(model.Weight, model.Unit), Date = model.Date };

    private static async Task<IReadOnlyList<WorkoutMainRecordPersistenceModel>> ReadAsync(IQueryable<MainRecord> query, CancellationToken cancellationToken)
        => (await query.AsNoTracking().ToListAsync(cancellationToken)).Select(WorkoutPersistenceProjection.MainRecord).ToList();
}
