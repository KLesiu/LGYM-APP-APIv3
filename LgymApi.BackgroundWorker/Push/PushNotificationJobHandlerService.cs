using LgymApi.Application.Notifications;

namespace LgymApi.BackgroundWorker.Push;

public sealed class PushNotificationJobHandlerService
{
    private readonly IPushNotificationDeliveryService _pushNotificationDeliveryService;

    public PushNotificationJobHandlerService(IPushNotificationDeliveryService pushNotificationDeliveryService)
    {
        _pushNotificationDeliveryService = pushNotificationDeliveryService;
    }

    public Task ProcessAsync(string notificationId, CancellationToken cancellationToken = default)
        => _pushNotificationDeliveryService.ProcessAsync(notificationId, cancellationToken);
}
