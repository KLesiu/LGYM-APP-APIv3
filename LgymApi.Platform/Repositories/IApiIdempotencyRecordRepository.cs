using LgymApi.Domain.Entities;

namespace LgymApi.Application.Repositories;

/// <summary>
/// Repository for managing API-layer idempotency records to support replay-safe duplicate request handling.
/// </summary>
public interface IApiIdempotencyRecordRepository
{
    /// <summary>
    /// Finds an existing idempotency record by scope tuple and idempotency key.
    /// Used for replay detection and conflict checking.
    /// </summary>
    Task<ApiIdempotencyRecord?> FindByScopeAndKeyAsync(
        string scopeTuple,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to add a new idempotency record atomically.
    /// If a record with the same (ScopeTuple, IdempotencyKey) already exists, returns the existing record.
    /// If the new record is successfully added, returns the newly created record.
    /// Uses database-level uniqueness constraint to ensure duplicate-safe persistence.
    /// </summary>
    Task<ApiIdempotencyRecord> AddOrGetExistingAsync(
        ApiIdempotencyRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing idempotency record. Used to persist response snapshot after first execution.
    /// </summary>
    Task UpdateAsync(
        ApiIdempotencyRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts idempotency records with in-progress state (ResponseStatusCode = 0).
    /// Used for operational visibility of concurrent requests being processed.
    /// </summary>
    Task<int> CountInProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts idempotency records by response status code for operational metrics.
    /// </summary>
    Task<int> CountByStatusCodeAsync(int statusCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes idempotency records older than the specified cutoff date.
    /// Bounded cleanup prevents unbounded table growth while preserving recent audit trail.
    /// Returns the count of deleted records.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default);
}
