using LgymApi.Application.Notifications;

namespace LgymApi.Application.Notifications.Providers.Fcm;

internal sealed class PushInstallationCleanupSettings : IStalePushInstallationCleanupSettings
{
    private readonly PushNotificationOptions _options;

    public PushInstallationCleanupSettings(PushNotificationOptions options)
    {
        _options = options;
    }

    public bool Enabled => _options.StaleTokenCleanupEnabled;
    public int InactivityDays => _options.StaleTokenInactivityDays;
    public int BatchSize => _options.StaleTokenCleanupBatchSize;
}
