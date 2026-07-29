using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.TrainingPlanning;

internal static class AccountReadServiceExtensions
{
    internal static Task<AccountReadModel?> GetByIdAsync(
        this IAccountReadService accountReadService,
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
    {
        return accountReadService.GetByIdAsync(accountId.Rebind<User>(), cancellationToken);
    }
}
