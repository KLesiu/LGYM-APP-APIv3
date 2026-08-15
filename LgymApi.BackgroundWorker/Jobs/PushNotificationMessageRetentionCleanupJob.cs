using LgymApi.Application.Notifications;
using LgymApi.BackgroundWorker.Common.Jobs;

namespace LgymApi.BackgroundWorker.Jobs;

public sealed class PushNotificationMessageRetentionCleanupJob : IPushNotificationMessageRetentionCleanupJob
{
    private readonly IPushNotificationMessageRetentionCleanupService _cleanupService;

    public PushNotificationMessageRetentionCleanupJob(IPushNotificationMessageRetentionCleanupService cleanupService)
    {
        _cleanupService = cleanupService;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await _cleanupService.CleanupAsync(cancellationToken);
    }
}
