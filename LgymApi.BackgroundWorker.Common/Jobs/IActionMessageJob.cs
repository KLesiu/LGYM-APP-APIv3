namespace LgymApi.BackgroundWorker.Common.Jobs;

/// <summary>
/// Hangfire job interface for background action message orchestration.
/// Processes one durable command envelope by id.
/// </summary>
public interface IActionMessageJob
{
    /// <summary>
    /// Executes the orchestrated processing of a background action message.
    /// </summary>
    /// <param name="actionMessageId">The durable action message envelope id.</param>
    Task ExecuteAsync(string actionMessageId);
}
