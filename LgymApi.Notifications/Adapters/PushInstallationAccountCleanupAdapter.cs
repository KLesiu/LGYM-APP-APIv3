using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Identity.Contracts;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications.Adapters;

internal sealed class PushInstallationAccountCleanupAdapter(
    IPushInstallationLifecycleService pushInstallationLifecycleService) : IAccountPushInstallationCleanupPort
{
    public Task StageRemoveForAccountAsync(
        Id<AccountReference> accountId,
        CancellationToken cancellationToken = default)
    {
        return pushInstallationLifecycleService.StageRemoveForAccountAsync(accountId, cancellationToken);
    }
}
