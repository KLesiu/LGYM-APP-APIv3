using LgymApi.Application.Notifications.Providers.Fcm;

namespace LgymApi.Application.Notifications;

internal sealed class NotificationRetentionSettings : INotificationRetentionSettings
{
    private readonly PushNotificationOptions _options;

    public NotificationRetentionSettings(PushNotificationOptions options)
    {
        _options = options;
    }

    public int MessageHistoryDays => _options.MessageHistoryDays;
    public int DisabledInstallationDays => _options.DisabledInstallationDays;
    public int InAppNotificationDays => _options.InAppNotificationDays;
    public int BatchSize => _options.RetentionPurgeBatchSize;
}
