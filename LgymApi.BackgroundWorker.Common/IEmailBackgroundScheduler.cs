namespace LgymApi.BackgroundWorker.Common;

public interface IEmailBackgroundScheduler
{
    string? Enqueue(string notificationId);
}
