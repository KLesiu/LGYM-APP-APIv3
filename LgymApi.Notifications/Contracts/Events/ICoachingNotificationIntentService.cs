namespace LgymApi.Application.Notifications.Contracts.Events;

public interface ICoachingNotificationIntentService
{
    Task<CoachingNotificationIntentResult> SubmitAsync(
        CoachingNotificationIntent intent,
        CancellationToken cancellationToken = default);
}
