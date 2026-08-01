using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Repositories;

internal interface IPasswordResetTokenRepository
{
    Task AddAsync(PasswordResetToken token, CancellationToken cancellationToken = default);
    Task<PasswordResetToken?> FindActiveByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<List<PasswordResetToken>> GetActiveForUserAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task UpdateAsync(PasswordResetToken token, CancellationToken cancellationToken = default);
    Task<bool> TokenHashExistsAsync(string tokenHash, CancellationToken cancellationToken = default);
}
