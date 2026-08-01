namespace LgymApi.BackgroundWorker.Common.Notifications;

public interface IEmailJobHandler
{
    Task ProcessAsync(string notificationId, CancellationToken cancellationToken = default);
}
