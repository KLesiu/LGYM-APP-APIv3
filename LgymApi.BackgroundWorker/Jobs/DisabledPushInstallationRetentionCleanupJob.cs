using LgymApi.Application.Notifications;
using LgymApi.BackgroundWorker.Common.Jobs;

namespace LgymApi.BackgroundWorker.Jobs;

public sealed class DisabledPushInstallationRetentionCleanupJob : IDisabledPushInstallationRetentionCleanupJob
{
    private readonly IDisabledPushInstallationRetentionCleanupService _cleanupService;

    public DisabledPushInstallationRetentionCleanupJob(IDisabledPushInstallationRetentionCleanupService cleanupService)
    {
        _cleanupService = cleanupService;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await _cleanupService.CleanupAsync(cancellationToken);
    }
}
