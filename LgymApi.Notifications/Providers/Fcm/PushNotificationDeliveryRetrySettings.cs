using LgymApi.Application.Notifications.Contracts.Push;

namespace LgymApi.Application.Notifications.Providers.Fcm;

internal sealed class PushNotificationDeliveryRetrySettings : IPushNotificationDeliveryRetrySettings
{
    private readonly PushNotificationOptions _options;

    public PushNotificationDeliveryRetrySettings(PushNotificationOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<int> RetryDelaysSeconds => _options.RetryDelaysSeconds;
}
