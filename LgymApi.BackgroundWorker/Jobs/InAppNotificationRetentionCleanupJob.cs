using LgymApi.Application.Notifications;
using LgymApi.BackgroundWorker.Common.Jobs;

namespace LgymApi.BackgroundWorker.Jobs;

public sealed class InAppNotificationRetentionCleanupJob : IInAppNotificationRetentionCleanupJob
{
    private readonly IInAppNotificationRetentionCleanupService _cleanupService;

    public InAppNotificationRetentionCleanupJob(IInAppNotificationRetentionCleanupService cleanupService)
    {
        _cleanupService = cleanupService;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await _cleanupService.CleanupAsync(cancellationToken);
    }
}
