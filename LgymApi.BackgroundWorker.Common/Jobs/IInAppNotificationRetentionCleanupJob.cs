namespace LgymApi.BackgroundWorker.Common.Jobs;

public interface IInAppNotificationRetentionCleanupJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
