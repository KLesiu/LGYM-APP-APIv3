using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Repositories;

/// <summary>
/// Repository for managing CommandEnvelope entities with idempotency and retry orchestration queries.
/// </summary>
public interface ICommandEnvelopeRepository
{
    /// <summary>
    /// Adds a new command envelope to the repository.
    /// </summary>
    Task AddAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a command envelope by its unique identifier.
    /// </summary>
    Task<CommandEnvelope?> FindByIdAsync(Id<CommandEnvelope> id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a command envelope by correlation ID for idempotency checking.
    /// Used to prevent duplicate command execution for the same logical operation.
    /// </summary>
    Task<CommandEnvelope?> FindByCorrelationIdAsync(Id<CorrelationScope> correlationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all command envelopes with status Failed that have NextAttemptAt in the past and should be retried.
    /// Used by the orchestrator to poll for pending retry candidates.
    /// </summary>
    Task<List<CommandEnvelope>> GetPendingRetriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing command envelope. Used after status transitions or attempt recording.
    /// </summary>
    Task UpdateAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops tracking a failed insert so the same unit of work can load the winning duplicate envelope.
    /// </summary>
    void Detach(CommandEnvelope envelope);

    /// <summary>
    /// Attempts to add a command envelope atomically, preventing duplicate correlation IDs.
    /// If an envelope with the same correlation ID already exists, returns the existing envelope.
    /// If the new envelope is successfully added, returns the newly created envelope.
    /// Uses database-level uniqueness constraint (or conflict detection) to ensure durable idempotency.
    /// </summary>
    /// <param name="envelope">The command envelope to add (must have unique CorrelationId)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The newly created envelope if added, or the existing envelope if duplicate CorrelationId found</returns>
    Task<CommandEnvelope> AddOrGetExistingAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default);

    Task<CommandEnvelope?> TryRecoverDuplicateAsync(
        CommandEnvelope stagedEnvelope,
        Exception commitFailure,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CommandEnvelope?>(null);
    }

    /// <summary>
    /// Retrieves all command envelopes with Pending status that have not yet been dispatched.
    /// Used for operational observability of queued-but-not-yet-scheduled work.
    /// </summary>
    Task<List<CommandEnvelope>> GetPendingUndispatchedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all command envelopes with Failed status for operational visibility.
    /// Includes both retry-eligible failures and those awaiting retry scheduling.
    /// </summary>
    Task<List<CommandEnvelope>> GetFailedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all dead-lettered command envelopes (terminal poison state).
    /// Used for operational alerts and troubleshooting stranded work items.
    /// </summary>
    Task<List<CommandEnvelope>> GetDeadLetteredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts command envelopes by status for operational metrics and reporting.
    /// </summary>
    Task<int> CountByStatusAsync(ActionExecutionStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes completed command envelopes older than the specified cutoff date.
    /// Bounded cleanup prevents unbounded table growth while preserving recent audit trail.
    /// Returns the count of deleted records.
    /// </summary>
    Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default);
}
