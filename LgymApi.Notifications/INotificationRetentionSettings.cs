namespace LgymApi.Application.Notifications;

internal interface INotificationRetentionSettings
{
    int MessageHistoryDays { get; }
    int DisabledInstallationDays { get; }
    int InAppNotificationDays { get; }
    int BatchSize { get; }
}
