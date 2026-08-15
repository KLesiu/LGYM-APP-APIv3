using Hangfire;
using LgymApi.BackgroundWorker.Common.Jobs;

namespace LgymApi.BackgroundWorker;

public static class BackgroundWorkerRecurringJobs
{
    public static void Configure()
    {
        RecurringJob.AddOrUpdate<ICommittedIntentDispatchJob>("reliability-committed-intent-dispatch", job => job.ExecuteAsync(CancellationToken.None), Cron.Minutely);
        RecurringJob.AddOrUpdate<IExpiredPhotoUploadCleanupJob>("reporting-expired-photo-upload-cleanup", job => job.ExecuteAsync(CancellationToken.None), Cron.Minutely);
        RecurringJob.AddOrUpdate<IRecurringReportAssignmentProcessingJob>("reporting-recurring-report-assignments", job => job.ExecuteAsync(CancellationToken.None), Cron.Minutely);
        RecurringJob.AddOrUpdate<IStalePushInstallationCleanupJob>("push-stale-installation-cleanup", job => job.ExecuteAsync(CancellationToken.None), Cron.Daily(3));
        RecurringJob.AddOrUpdate<IPushNotificationMessageRetentionCleanupJob>("push-notification-message-retention-cleanup", job => job.ExecuteAsync(CancellationToken.None), Cron.Daily());
        RecurringJob.AddOrUpdate<IDisabledPushInstallationRetentionCleanupJob>("push-disabled-installation-retention-cleanup", job => job.ExecuteAsync(CancellationToken.None), Cron.Daily());
        RecurringJob.AddOrUpdate<IInAppNotificationRetentionCleanupJob>("in-app-notification-retention-cleanup", job => job.ExecuteAsync(CancellationToken.None), Cron.Daily());
    }
}
