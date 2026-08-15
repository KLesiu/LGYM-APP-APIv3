using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Identity.Contracts.Accounts;

public interface IAccountPushInstallationCleanupPort
{
    Task StageRemoveForAccountAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default);
}
