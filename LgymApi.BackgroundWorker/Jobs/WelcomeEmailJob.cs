using Hangfire;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Jobs;

namespace LgymApi.Infrastructure.Jobs;

public sealed class WelcomeEmailJob : IWelcomeEmailJob
{
    private readonly IEmailJobHandler _handler;

    public WelcomeEmailJob(IEmailJobHandler handler)
    {
        _handler = handler;
    }

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public Task ExecuteAsync(string notificationId)
    {
        return _handler.ProcessAsync(notificationId);
    }
}
