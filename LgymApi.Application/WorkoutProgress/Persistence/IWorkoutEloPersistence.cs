using LgymApi.Identity.Contracts;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.Persistence;

public interface IWorkoutEloPersistence
{
    Task AddAsync(WorkoutEloWriteModel registry, CancellationToken cancellationToken = default);
    Task CreateInitialForAccountAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<int?> GetLatestEloAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<WorkoutEloPersistenceModel?> GetLatestEntryAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutEloPersistenceModel>> GetByAccountIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
}
