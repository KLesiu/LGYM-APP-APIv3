using LgymApi.Application.Notifications.Models;

namespace LgymApi.Application.Notifications;

internal sealed class NotificationEventBridge : INotificationEventBridge
{
    private readonly IPushNotificationService _pushNotificationService;

    public NotificationEventBridge(IPushNotificationService pushNotificationService)
    {
        _pushNotificationService = pushNotificationService;
    }

    public Task EnqueueAsync(EnqueueNotificationEventInput input, CancellationToken cancellationToken = default)
    {
        return _pushNotificationService.EnqueueAsync(
            new EnqueuePushNotificationInput(
                input.UserId,
                input.SchemaVersion,
                input.Type,
                input.EventKey,
                input.EntityKey,
                input.InAppNotificationId,
                input.Deeplink),
            cancellationToken);
    }
}
