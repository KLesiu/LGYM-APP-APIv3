namespace LgymApi.Application.Notifications.Contracts.Events;

public interface ICoachingEmailNotificationScheduler
{
    Task ScheduleAsync(
        CoachingEmailSchedulingRequest request,
        CancellationToken cancellationToken = default);
}
