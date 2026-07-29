namespace LgymApi.Application.Notifications.Contracts.Push;

public interface IPushBackgroundScheduler
{
    string? Enqueue(string notificationId);
    string? ScheduleRetry(string notificationId, TimeSpan delay);
}
