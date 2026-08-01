using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Repositories;

public interface IEloRegistryRepository
{
    Task AddAsync(EloRegistry registry, CancellationToken cancellationToken = default);
    Task CreateInitialForUserAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<int?> GetLatestEloAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<EloRegistry?> GetLatestEntryAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<List<EloRegistry>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default);
}
