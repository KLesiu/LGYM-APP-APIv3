using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

public sealed class EloRegistryRepository : IEloRegistryRepository
{
    private readonly AppDbContext _dbContext;

    public EloRegistryRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(EloRegistry registry, CancellationToken cancellationToken = default)
    {
        return _dbContext.EloRegistries.AddAsync(registry, cancellationToken).AsTask();
    }

    public Task CreateInitialForUserAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.EloRegistries.AddAsync(
            new EloRegistry
            {
                Id = Id<EloRegistry>.New(),
                UserId = userId,
                Date = DateTimeOffset.UtcNow,
                Elo = 1000
            },
            cancellationToken).AsTask();
    }

    public Task<int?> GetLatestEloAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.EloRegistries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Date)
            .Select(e => (int?)e.Elo)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<EloRegistry?> GetLatestEntryAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.EloRegistries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.Date)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<EloRegistry>> GetByUserIdAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.EloRegistries
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.Date)
            .ToListAsync(cancellationToken);
    }
}
