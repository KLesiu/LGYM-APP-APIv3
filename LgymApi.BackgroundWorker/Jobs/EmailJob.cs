using Hangfire;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Jobs;

namespace LgymApi.Infrastructure.Jobs;

public sealed class EmailJob : IEmailJob
{
    private readonly IEmailJobHandler _handler;

    public EmailJob(IEmailJobHandler handler)
    {
        _handler = handler;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    [DisableConcurrentExecution(60)]
    public Task ExecuteAsync(string notificationId)
    {
        return _handler.ProcessAsync(notificationId);
    }
}
