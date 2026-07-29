using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.Persistence;

public interface IWorkoutMainRecordPersistence
{
    Task AddAsync(WorkoutMainRecordWriteModel record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutMainRecordPersistenceModel>> GetByAccountIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutMainRecordPersistenceModel>> GetBestByAccountGroupedByExerciseAndUnitAsync(Id<AccountReference> accountId, IReadOnlyCollection<Id<LgymApi.Domain.Entities.Exercise>>? exerciseIds = null, CancellationToken cancellationToken = default);
    Task<WorkoutMainRecordPersistenceModel?> FindByIdAsync(Id<LgymApi.Domain.Entities.MainRecord> id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Id<LgymApi.Domain.Entities.MainRecord> id, CancellationToken cancellationToken = default);
    Task UpdateAsync(WorkoutMainRecordWriteModel record, CancellationToken cancellationToken = default);
}
