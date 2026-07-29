using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.UnitTests;

internal sealed class FakeAccountLookupService(params AccountLookup[] accounts) : IAccountLookupService
{
    private readonly Dictionary<Id<AccountReference>, AccountLookup> _accounts =
        accounts.ToDictionary(account => account.Id);

    public Task<AccountLookup?> GetByIdAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_accounts.GetValueOrDefault(accountId));

    public Task<AccountLookup?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(_accounts.Values.FirstOrDefault(account => account.Email == email));

    public Task<IReadOnlyList<AccountLookup>> GetByIdsAsync(
        IReadOnlyList<Id<AccountReference>> accountIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AccountLookup>>(
            accountIds.Where(_accounts.ContainsKey).Select(accountId => _accounts[accountId]).ToList());
}
