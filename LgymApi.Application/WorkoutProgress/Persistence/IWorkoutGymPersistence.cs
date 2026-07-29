using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.Persistence;

public interface IWorkoutGymPersistence
{
    Task AddAsync(WorkoutGymWriteModel gym, CancellationToken cancellationToken = default);
    Task<WorkoutGymPersistenceModel?> FindByIdAsync(Id<LgymApi.Domain.Entities.Gym> id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutGymPersistenceModel>> GetByAccountIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task UpdateAsync(WorkoutGymWriteModel gym, CancellationToken cancellationToken = default);
}
