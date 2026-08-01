namespace LgymApi.BackgroundWorker.Common.Jobs;

public interface IInvitationEmailJob
{
    Task ExecuteAsync(string notificationId);
}
