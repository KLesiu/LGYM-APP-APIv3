namespace LgymApi.Application.Notifications;

public interface IDisabledPushInstallationRetentionCleanupService
{
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}
