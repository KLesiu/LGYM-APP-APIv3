using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Identity.Contracts;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications.Adapters;

internal sealed class PushInstallationSessionDisassociationAdapter(
    IPushInstallationLifecycleService pushInstallationLifecycleService) : IAccountSessionDisassociationPort
{
    public Task StageDisassociateAsync(
        Id<AccountReference> accountId,
        Id<AccountSessionReference> sessionId,
        CancellationToken cancellationToken = default)
    {
        return pushInstallationLifecycleService.StageDisassociateForSessionAsync(
            sessionId,
            cancellationToken);
    }
}
