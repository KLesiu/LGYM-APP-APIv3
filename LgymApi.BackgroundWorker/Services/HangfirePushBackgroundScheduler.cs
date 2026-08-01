using Hangfire;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.BackgroundWorker.Common.Jobs;

namespace LgymApi.BackgroundWorker.Services;

public sealed class HangfirePushBackgroundScheduler : IPushBackgroundScheduler
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public HangfirePushBackgroundScheduler(IBackgroundJobClient backgroundJobClient)
    {
        _backgroundJobClient = backgroundJobClient;
    }

    public string? Enqueue(string notificationId)
    {
        return _backgroundJobClient.Enqueue<IPushNotificationJob>(job => job.ExecuteAsync(notificationId, CancellationToken.None));
    }

    public string? ScheduleRetry(string notificationId, TimeSpan delay)
    {
        return _backgroundJobClient.Schedule<IPushNotificationJob>(job => job.ExecuteAsync(notificationId, CancellationToken.None), delay);
    }
}
