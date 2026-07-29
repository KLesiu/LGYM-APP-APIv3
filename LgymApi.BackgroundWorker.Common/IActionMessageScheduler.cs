namespace LgymApi.BackgroundWorker.Common;

/// <summary>
/// Scheduler adapter for background action message processing.
/// Enqueues jobs by durable command envelope id only.
/// </summary>
public interface IActionMessageScheduler
{
    /// <summary>
    /// Enqueues a background action message for orchestrated processing.
    /// </summary>
    /// <param name="actionMessageId">The durable action message envelope id.</param>
    string? Enqueue(string actionMessageId);
}
