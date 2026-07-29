namespace LgymApi.Application.Notifications.Contracts.Push;

public interface IPushNotificationDeliveryRetrySettings
{
    IReadOnlyList<int> RetryDelaysSeconds { get; }
}
