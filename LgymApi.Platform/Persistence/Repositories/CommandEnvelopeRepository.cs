using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class CommandEnvelopeRepository : ICommandEnvelopeRepository
{
    private readonly IPlatformPersistenceContext _persistenceContext;
    private readonly ICommandEnvelopeDuplicateFailureClassifier _duplicateFailureClassifier;

    public CommandEnvelopeRepository(
        IPlatformPersistenceContext persistenceContext,
        ICommandEnvelopeDuplicateFailureClassifier duplicateFailureClassifier)
    {
        _persistenceContext = persistenceContext;
        _duplicateFailureClassifier = duplicateFailureClassifier;
    }

    public async Task AddAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        await _persistenceContext.CommandEnvelopes.AddAsync(envelope, cancellationToken);
    }

    public Task<CommandEnvelope?> FindByIdAsync(Id<CommandEnvelope> id, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.CommandEnvelopes
            .Include(e => e.ExecutionLogs)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public Task<CommandEnvelope?> FindByCorrelationIdAsync(Id<CorrelationScope> correlationId, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.CommandEnvelopes
            .Include(e => e.ExecutionLogs)
            .FirstOrDefaultAsync(x => x.CorrelationId == correlationId, cancellationToken);
    }

    public async Task<List<CommandEnvelope>> GetPendingRetriesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await _persistenceContext.CommandEnvelopes
            .Include(e => e.ExecutionLogs)
            .Where(x => x.Status == ActionExecutionStatus.Failed
                        && x.NextAttemptAt != null
                        && x.NextAttemptAt <= now)
            .ToListAsync(cancellationToken);
    }

    public Task UpdateAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        _persistenceContext.CommandEnvelopes.Update(envelope);
        return Task.CompletedTask;
    }

    public void Detach(CommandEnvelope envelope)
    {
        _persistenceContext.Entry(envelope).State = EntityState.Detached;
    }

    /// <summary>
    /// Adds a command envelope or returns the existing one if duplicate.
    /// 
    /// Implementation strategy:
    /// 1. Check for existing envelope by CorrelationId (read phase)
    /// 2. If exists, return it immediately (idempotent path)
    /// 3. If not exists, stage new envelope for insert (write phase)
    /// 
    /// Duplicate protection relies on DB unique constraint (IX_CommandEnvelopes_CorrelationId).
    /// Concurrent duplicate attempts will be rejected at SaveChangesAsync (caller responsibility).
    /// Caller must handle DbUpdateException and retry by calling this method again to fetch existing.
    /// 
    /// This is a "stage-then-persist" pattern aligned with Unit of Work discipline:
    /// - Repository stages changes but does not call SaveChanges
    /// - Caller controls transaction boundary and handles constraint violations
    /// - On constraint violation, caller detaches failed entity and retries this method
    /// </summary>
    public async Task<CommandEnvelope> AddOrGetExistingAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        // Read phase: check for existing envelope by unique CorrelationId
        var existing = await FindByCorrelationIdAsync(envelope.CorrelationId, cancellationToken);
        if (existing != null)
        {
            return existing; // Idempotent: duplicate detected, return existing
        }

        // Write phase: stage new envelope for insert (caller will persist via SaveChangesAsync)
        await AddAsync(envelope, cancellationToken);
        return envelope;
    }

    public Task<CommandEnvelope?> TryRecoverDuplicateAsync(
        CommandEnvelope stagedEnvelope,
        Exception commitFailure,
        CancellationToken cancellationToken = default)
    {
        if (!_duplicateFailureClassifier.IsDuplicateCorrelationFailure(commitFailure))
        {
            return Task.FromResult<CommandEnvelope?>(null);
        }

        _persistenceContext.Entry(stagedEnvelope).State = EntityState.Detached;
        return FindByCorrelationIdAsync(stagedEnvelope.CorrelationId, cancellationToken);
    }

    /// <summary>
    /// Retrieves all command envelopes with Pending status that have not yet been dispatched.
    /// Used for operational observability of queued-but-not-yet-scheduled work.
    /// </summary>
    public async Task<List<CommandEnvelope>> GetPendingUndispatchedAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.CommandEnvelopes
            .Include(e => e.ExecutionLogs)
            .Where(x => x.Status == ActionExecutionStatus.Pending && x.DispatchedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves all command envelopes with Failed status for operational visibility.
    /// Includes both retry-eligible failures and those awaiting retry scheduling.
    /// </summary>
    public async Task<List<CommandEnvelope>> GetFailedAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.CommandEnvelopes
            .Include(e => e.ExecutionLogs)
            .Where(x => x.Status == ActionExecutionStatus.Failed)
            .OrderBy(x => x.LastAttemptAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves all dead-lettered command envelopes (terminal poison state).
    /// Used for operational alerts and troubleshooting stranded work items.
    /// </summary>
    public async Task<List<CommandEnvelope>> GetDeadLetteredAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.CommandEnvelopes
            .Include(e => e.ExecutionLogs)
            .Where(x => x.Status == ActionExecutionStatus.DeadLettered)
            .OrderBy(x => x.LastAttemptAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Counts command envelopes by status for operational metrics and reporting.
    /// </summary>
    public async Task<int> CountByStatusAsync(ActionExecutionStatus status, CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.CommandEnvelopes
            .Where(x => x.Status == status)
            .CountAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes completed command envelopes older than the specified cutoff date.
    /// Bounded cleanup prevents unbounded table growth while preserving recent audit trail.
    /// Returns the count of deleted records.
    /// </summary>
    public async Task<int> DeleteCompletedOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default)
    {
        var envelopesToDelete = await _persistenceContext.CommandEnvelopes
            .Where(x => x.Status == ActionExecutionStatus.Completed && x.CompletedAt != null && x.CompletedAt < cutoffDate)
            .ToListAsync(cancellationToken);

        foreach (var envelope in envelopesToDelete)
        {
            _persistenceContext.CommandEnvelopes.Remove(envelope);
        }

        return envelopesToDelete.Count;
    }
}
