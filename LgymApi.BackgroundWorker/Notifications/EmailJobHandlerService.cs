using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.BackgroundWorker.Common.Notifications;

namespace LgymApi.BackgroundWorker.Notifications;

public sealed class EmailJobHandlerService(IEmailJobExecutionPort port) : IEmailJobHandler
{
    public Task ProcessAsync(string notificationId, CancellationToken cancellationToken = default) =>
        port.ProcessAsync(notificationId, cancellationToken);
}
