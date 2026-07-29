using LgymApi.Domain.ValueObjects;

namespace LgymApi.Identity.Contracts.Accounts;

public sealed record AccountLookup(
    Id<AccountReference> Id,
    string Name,
    string Email,
    string? Avatar,
    string PreferredLanguage,
    string PreferredTimeZone,
    DateTimeOffset CreatedAt);

public interface IAccountLookupService
{
    Task<AccountLookup?> GetByIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<AccountLookup?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountLookup>> GetByIdsAsync(
        IReadOnlyList<Id<AccountReference>> accountIds,
        CancellationToken cancellationToken = default);
}
