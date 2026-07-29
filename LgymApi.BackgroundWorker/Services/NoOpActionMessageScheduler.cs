using LgymApi.BackgroundWorker.Common;

namespace LgymApi.Infrastructure.Services;

/// <summary>
/// No-op scheduler for test mode. Does not enqueue any jobs.
/// </summary>
public sealed class NoOpActionMessageScheduler : IActionMessageScheduler
{
    public string? Enqueue(string actionMessageId)
    {
        return $"noop-command-{actionMessageId}";
    }
}
