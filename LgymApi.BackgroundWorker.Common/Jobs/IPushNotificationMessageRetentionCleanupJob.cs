namespace LgymApi.BackgroundWorker.Common.Jobs;

public interface IPushNotificationMessageRetentionCleanupJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
