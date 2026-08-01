using System.Collections.Concurrent;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.IntegrationTests;

public sealed class TestPushProviderSender : IPushProviderSender
{
    private readonly ConcurrentQueue<(Id<PushInstallation> InstallationId, PushEventPayload Payload)> _attempts = new();

    public IReadOnlyCollection<(Id<PushInstallation> InstallationId, PushEventPayload Payload)> Attempts
        => _attempts.ToArray();

    public Task<PushSendAttemptResult> SendAsync(
        Id<PushInstallation> installationId,
        PushEventPayload payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _attempts.Enqueue((installationId, payload));

        return Task.FromResult(new PushSendAttemptResult(
            PushSendOutcome.Skipped,
            "TestCapture",
            null,
            null,
            null));
    }
}
