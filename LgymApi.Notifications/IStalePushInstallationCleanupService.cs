namespace LgymApi.Application.Notifications;

public interface IStalePushInstallationCleanupService
{
    Task<int> CleanupAsync(CancellationToken cancellationToken = default);
}
