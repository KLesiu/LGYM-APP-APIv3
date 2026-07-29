namespace LgymApi.BackgroundWorker.Common.Jobs;

public interface IPushNotificationJob
{
    Task ExecuteAsync(string notificationId, CancellationToken cancellationToken = default);
}
