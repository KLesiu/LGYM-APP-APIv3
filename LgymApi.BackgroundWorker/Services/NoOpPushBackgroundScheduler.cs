using LgymApi.Application.Notifications.Contracts.Push;

namespace LgymApi.BackgroundWorker.Services;

public sealed class NoOpPushBackgroundScheduler : IPushBackgroundScheduler
{
    public string? Enqueue(string notificationId)
    {
        return null;
    }

    public string? ScheduleRetry(string notificationId, TimeSpan delay)
    {
        return null;
    }
}
