using LgymApi.Application.Notifications.Models;

namespace LgymApi.Application.Notifications;

public interface INotificationEventBridge
{
    Task EnqueueAsync(EnqueueNotificationEventInput input, CancellationToken cancellationToken = default);
}
