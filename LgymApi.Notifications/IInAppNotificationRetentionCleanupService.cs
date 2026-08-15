namespace LgymApi.Application.Notifications;

public interface IInAppNotificationRetentionCleanupService
{
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}
