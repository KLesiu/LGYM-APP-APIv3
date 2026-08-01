using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.WorkoutProgress;

public sealed class WorkoutMeasurementPersistenceRepository(AppDbContext dbContext) : IWorkoutMeasurementPersistence
{
    public Task AddAsync(WorkoutMeasurementWriteModel measurement, CancellationToken cancellationToken = default)
        => dbContext.Measurements.AddAsync(new Measurement { Id = measurement.Id, UserId = WorkoutPersistenceAccountIds.ToPersisted(measurement.AccountId), BodyPart = measurement.BodyPart, Unit = measurement.Unit, Value = measurement.Value, CreatedAt = measurement.CreatedAt ?? default }, cancellationToken).AsTask();

    public async Task<WorkoutMeasurementPersistenceModel?> FindByIdAsync(Id<Measurement> id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Measurements.AsNoTracking().FirstOrDefaultAsync(measurement => measurement.Id == id, cancellationToken);
        return entity is null ? null : WorkoutPersistenceProjection.Measurement(entity);
    }

    public async Task<IReadOnlyList<WorkoutMeasurementPersistenceModel>> GetByAccountAsync(Id<AccountReference> accountId, BodyParts? bodyPart, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Measurements.AsNoTracking().Where(measurement => measurement.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId));
        if (bodyPart.HasValue) query = query.Where(measurement => measurement.BodyPart == bodyPart.Value);
        return (await query.ToListAsync(cancellationToken)).Select(WorkoutPersistenceProjection.Measurement).ToList();
    }
}
