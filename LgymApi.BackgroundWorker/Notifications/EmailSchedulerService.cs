using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.BackgroundWorker.Common.Notifications;

namespace LgymApi.BackgroundWorker.Notifications;

public sealed class EmailSchedulerService<TPayload>(IEmailSchedulingPort<TPayload> port) : IEmailScheduler<TPayload>
    where TPayload : IEmailPayload
{
    public Task ScheduleAsync(TPayload payload, CancellationToken cancellationToken = default) =>
        port.ScheduleAsync(payload, cancellationToken);
}
