using LgymApi.Application.Notifications.Models;

namespace LgymApi.Application.Notifications;

public interface IPushNotificationService
{
    Task EnqueueAsync(EnqueuePushNotificationInput input, CancellationToken cancellationToken = default);
}
