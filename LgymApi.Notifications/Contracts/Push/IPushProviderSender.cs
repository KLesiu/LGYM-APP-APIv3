using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications.Contracts.Push;

public interface IPushProviderSender
{
    Task<PushSendAttemptResult> SendAsync(
        Id<PushInstallation> installationId,
        PushEventPayload payload,
        CancellationToken cancellationToken = default);
}
