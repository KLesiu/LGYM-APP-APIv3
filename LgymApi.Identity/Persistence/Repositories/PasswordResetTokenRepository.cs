using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly IIdentityPersistenceContext _dbContext;

    public PasswordResetTokenRepository(IIdentityPersistenceContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken = default)
    {
        return _dbContext.PasswordResetTokens.AddAsync(token, cancellationToken).AsTask();
    }

    public Task<PasswordResetToken?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return _dbContext.PasswordResetTokens
            .FirstOrDefaultAsync(
                t => t.TokenHash == tokenHash && !t.IsUsed && t.ExpiresAt > DateTimeOffset.UtcNow,
                cancellationToken);
    }

    public Task<List<PasswordResetToken>> GetActiveForUserAsync(Id<User> userId, CancellationToken cancellationToken = default)
    {
        return _dbContext.PasswordResetTokens
            .Where(t => t.UserId == userId && !t.IsUsed && !t.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public Task UpdateAsync(PasswordResetToken token, CancellationToken cancellationToken = default)
    {
        _dbContext.PasswordResetTokens.Update(token);
        return Task.CompletedTask;
    }

    public Task<bool> TokenHashExistsAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return _dbContext.PasswordResetTokens
            .AnyAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }
}
