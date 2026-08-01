using LgymApi.BackgroundWorker.Common;

namespace LgymApi.Infrastructure.Services;

public sealed class NoOpEmailBackgroundScheduler : IEmailBackgroundScheduler
{
    public string? Enqueue(string notificationId)
    {
        return $"noop-email-{notificationId}";
    }
}
