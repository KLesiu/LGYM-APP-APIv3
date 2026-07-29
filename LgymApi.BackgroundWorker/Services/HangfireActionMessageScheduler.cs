using Hangfire;
using LgymApi.BackgroundWorker.Common;
using LgymApi.BackgroundWorker.Common.Jobs;

namespace LgymApi.Infrastructure.Services;

/// <summary>
/// Hangfire-backed scheduler adapter for background action message processing.
/// Enqueues orchestration jobs by durable command envelope id only.
/// </summary>
public sealed class HangfireActionMessageScheduler : IActionMessageScheduler
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public HangfireActionMessageScheduler(IBackgroundJobClient backgroundJobClient)
    {
        _backgroundJobClient = backgroundJobClient;
    }

    public string? Enqueue(string actionMessageId)
    {
        return _backgroundJobClient.Enqueue<IActionMessageJob>(job => job.ExecuteAsync(actionMessageId));
    }
}
