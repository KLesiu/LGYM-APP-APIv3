using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Domain.Entities;

/// <summary>
/// Durable command envelope for storing background action commands with full lifecycle tracking.
/// Ensures idempotency and traceability of command execution.
/// </summary>
public sealed class CommandEnvelope : EntityBase<CommandEnvelope>
{

    // Retry policy configuration
    public const int MaxRetryAttempts = 3;
    public static readonly int[] RetryDelaysSeconds = { 60, 300, 900 }; // 1m, 5m, 15m
    /// <summary>
    /// Unique correlation identifier to link related commands and logs.
    /// </summary>
    public Id<CorrelationScope> CorrelationId { get; set; }

    /// <summary>
    /// Serialized command payload as JSON.
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// Canonical durable command ID used for exact command resolution.
    /// The legacy property and column name are retained for persistence compatibility.
    /// </summary>
    public string CommandTypeFullName { get; set; } = string.Empty;

    /// <summary>
    /// Current execution status of the envelope.
    /// </summary>
    public ActionExecutionStatus Status { get; set; } = ActionExecutionStatus.Pending;

    /// <summary>
    /// Timestamp when execution attempts should resume (for backoff/retry timing).
    /// Null if no next attempt is scheduled.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>
    /// Timestamp of the most recent execution attempt.
    /// </summary>
    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>
    /// Timestamp when the current processing lease started.
    /// Null when the envelope is not actively processing.
    /// </summary>
    public DateTimeOffset? ProcessingStartedAtUtc { get; set; }

    /// <summary>
    /// Timestamp when the envelope was first successfully processed.
    /// Null until status reaches Completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Timestamp when the envelope was dispatched to the background scheduler.
    /// Marks transition from Pending to Dispatched state in the durable-intent lifecycle.
    /// </summary>
    public DateTimeOffset? DispatchedAt { get; set; }

    /// <summary>
    /// Background scheduler job ID or queue reference assigned when dispatched.
    /// Enables correlation with external scheduler state (e.g., Hangfire job ID).
    /// </summary>
    public string? SchedulerJobId { get; set; }

    /// <summary>
    /// Related execution logs for this envelope (one-to-many).
    /// </summary>
    public ICollection<ActionExecutionLog> ExecutionLogs { get; set; } = new List<ActionExecutionLog>();

    /// <summary>
    /// Updates envelope state and schedules next retry based on attempt count.
    /// Does NOT add execution log (orchestrator records per-handler HandlerExecution logs).
    /// Does NOT mark as DeadLettered; caller must decide after this call.
    public void RecordAttemptFailure(string errorMessage, string? errorDetails = null)
    {
        if (Status == ActionExecutionStatus.DeadLettered)
        {
            throw new InvalidOperationException("Cannot record failure on dead-lettered envelope.");
        }

        var attemptNumber = GetExecutionAttemptCount();

        // No execution log added - orchestrator already recorded per-handler logs

        // Update envelope state
        Status = ActionExecutionStatus.Failed;
        LastAttemptAt = DateTimeOffset.UtcNow;
        ProcessingStartedAtUtc = null;

        // Schedule next attempt if we haven't exhausted all delay values
        // attemptNumber includes current failed attempt, so we use (attemptNumber - 1) as delay index
        var delayIndex = attemptNumber - 1;
        if (delayIndex >= 0 && delayIndex < RetryDelaysSeconds.Length)
        {
            var delaySeconds = RetryDelaysSeconds[delayIndex];
            NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
        }
        else
        {
            // Last attempt failed; will be marked dead-lettered by caller
            NextAttemptAt = null;
        }
    }

    /// <summary>
    /// Marks the envelope as dead-lettered after max retry attempts exceeded.
    /// Preserves all error history. Terminal state - cannot be transitioned further.
    /// </summary>
    public void MarkDeadLettered()
    {
        MarkDeadLettered("Dead-lettered after maximum retry attempts exceeded");
    }

    public void MarkDeadLettered(string reason, string? errorDetails = null)
    {
        if (Status == ActionExecutionStatus.DeadLettered)
        {
            return; // Already dead-lettered, idempotent
        }

        Status = ActionExecutionStatus.DeadLettered;
        LastAttemptAt = DateTimeOffset.UtcNow;
        NextAttemptAt = null;
        CompletedAt = DateTimeOffset.UtcNow;
        ProcessingStartedAtUtc = null;

        // Record dead-letter event in execution log
        var executionLog = new ActionExecutionLog
        {
            Id = Id<ActionExecutionLog>.New(),
            CommandEnvelopeId = Id,
            ActionType = ActionExecutionLogType.DeadLetter,
            Status = ActionExecutionStatus.DeadLettered,
            AttemptNumber = GetExecutionAttemptCount(),
            ErrorMessage = reason,
            ErrorDetails = errorDetails
        };
        ExecutionLogs.Add(executionLog);
    }

    /// <summary>
    /// Marks the envelope as completed successfully.
    /// Does NOT add execution log (orchestrator records per-handler HandlerExecution logs).
    public void MarkCompleted()
    {
        if (Status == ActionExecutionStatus.Completed || Status == ActionExecutionStatus.DeadLettered)
        {
            return; // Already terminal
        }

        Status = ActionExecutionStatus.Completed;
        LastAttemptAt = DateTimeOffset.UtcNow;
        CompletedAt = DateTimeOffset.UtcNow;
        NextAttemptAt = null;
        ProcessingStartedAtUtc = null;

        // No execution log added - orchestrator already recorded per-handler HandlerExecution logs
    }

    /// <summary>
    /// Determines if this envelope should be retried based on attempt count and status.
    /// </summary>
    public bool ShouldRetry()
    {
        if (Status != ActionExecutionStatus.Failed)
        {
            return false;
        }

        var executionAttemptCount = GetExecutionAttemptCount();
        return executionAttemptCount <= MaxRetryAttempts;
    }

    public int GetExecutionAttemptCount()
    {
        return ExecutionLogs.Count(log => log.ActionType == ActionExecutionLogType.Execute);
    }

    public void ResetStaleProcessing(string reason)
    {
        if (Status != ActionExecutionStatus.Processing)
        {
            return;
        }

        Status = ActionExecutionStatus.Pending;
        LastAttemptAt = DateTimeOffset.UtcNow;
        NextAttemptAt = DateTimeOffset.UtcNow;
        ProcessingStartedAtUtc = null;
        DispatchedAt = null;
        SchedulerJobId = null;

        ExecutionLogs.Add(new ActionExecutionLog
        {
            Id = Id<ActionExecutionLog>.New(),
            CommandEnvelopeId = Id,
            ActionType = ActionExecutionLogType.Execute,
            Status = ActionExecutionStatus.Failed,
            AttemptNumber = GetExecutionAttemptCount(),
            ErrorMessage = reason,
            ErrorDetails = reason
        });
    }
}
