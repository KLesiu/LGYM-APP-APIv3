namespace LgymApi.BackgroundWorker.Common.Jobs;

public interface IDisabledPushInstallationRetentionCleanupJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
