namespace LgymApi.Application.Notifications;

public interface IPushNotificationDeliveryService
{
    Task ProcessAsync(
        string notificationId,
        CancellationToken cancellationToken = default);
}
