using LgymApi.Application.Notifications.Models;

namespace LgymApi.Application.Notifications;

public interface IInAppNotificationPushPublisher
{
    Task PushAsync(InAppNotificationResult notification, CancellationToken ct = default);
}
