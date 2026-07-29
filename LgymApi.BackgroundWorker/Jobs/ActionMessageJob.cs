using Hangfire;
using LgymApi.BackgroundWorker.Common.Jobs;

namespace LgymApi.BackgroundWorker.Jobs;

/// <summary>
/// Hangfire job entrypoint for background action message orchestration.
/// Thin wrapper that delegates to the orchestrator handler service.
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
public sealed class ActionMessageJob : IActionMessageJob
{
    private readonly BackgroundActionOrchestratorService _orchestrator;

    public ActionMessageJob(BackgroundActionOrchestratorService orchestrator)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    /// <summary>
    /// Executes background action message orchestration for the given envelope id.
    /// </summary>
    /// <param name="actionMessageId">Durable command envelope id from persistent store</param>
    public async Task ExecuteAsync(string actionMessageId)
    {
        await _orchestrator.OrchestrateAsync(actionMessageId);
    }
}
