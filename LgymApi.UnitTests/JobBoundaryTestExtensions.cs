using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Common;
using LgymApi.BackgroundWorker.Common.Jobs;
using LgymApi.BackgroundWorker.Notifications;
using LgymApi.BackgroundWorker.Push;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Application.Notifications.Contracts.Push;

namespace LgymApi.UnitTests;

internal static class JobBoundaryTestExtensions
{
    public static Task OrchestrateAsync(this BackgroundActionOrchestratorService service, Id<CommandEnvelope> id, CancellationToken cancellationToken = default) =>
        service.OrchestrateAsync(id.ToString(), cancellationToken);

    public static Task ExecuteAsync(this IActionMessageJob job, Id<CommandEnvelope> id) => job.ExecuteAsync(id.ToString());
    public static Task ExecuteAsync(this IEmailJob job, Id<NotificationMessage> id) => job.ExecuteAsync(id.ToString());
    public static Task ExecuteAsync(this IInvitationEmailJob job, Id<NotificationMessage> id) => job.ExecuteAsync(id.ToString());
    public static Task ExecuteAsync(this IWelcomeEmailJob job, Id<NotificationMessage> id) => job.ExecuteAsync(id.ToString());
    public static Task ExecuteAsync(this IPushNotificationJob job, Id<PushNotificationMessage> id, CancellationToken cancellationToken = default) => job.ExecuteAsync(id.ToString(), cancellationToken);
    public static Task ProcessAsync(this PushNotificationJobHandlerService service, Id<PushNotificationMessage> id, CancellationToken cancellationToken = default) => service.ProcessAsync(id.ToString(), cancellationToken);
    public static string? Enqueue(this IActionMessageScheduler scheduler, Id<CommandEnvelope> id) => scheduler.Enqueue(id.ToString());
    public static string? Enqueue(this IEmailBackgroundScheduler scheduler, Id<NotificationMessage> id) => scheduler.Enqueue(id.ToString());
    public static string? Enqueue(this IPushBackgroundScheduler scheduler, Id<PushNotificationMessage> id) => scheduler.Enqueue(id.ToString());
    public static string? ScheduleRetry(this IPushBackgroundScheduler scheduler, Id<PushNotificationMessage> id, TimeSpan delay) => scheduler.ScheduleRetry(id.ToString(), delay);
}
