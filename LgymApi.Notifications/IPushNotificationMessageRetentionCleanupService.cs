namespace LgymApi.Application.Notifications;

public interface IPushNotificationMessageRetentionCleanupService
{
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}
