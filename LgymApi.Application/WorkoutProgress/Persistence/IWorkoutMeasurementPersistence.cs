using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.Persistence;

public interface IWorkoutMeasurementPersistence
{
    Task AddAsync(WorkoutMeasurementWriteModel measurement, CancellationToken cancellationToken = default);
    Task<WorkoutMeasurementPersistenceModel?> FindByIdAsync(Id<LgymApi.Domain.Entities.Measurement> id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutMeasurementPersistenceModel>> GetByAccountAsync(Id<AccountReference> accountId, BodyParts? bodyPart, CancellationToken cancellationToken = default);
}
