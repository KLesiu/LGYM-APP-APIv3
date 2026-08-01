using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class ApiIdempotencyRecordRepository : IApiIdempotencyRecordRepository
{
    private readonly IPlatformPersistenceContext _persistenceContext;

    public ApiIdempotencyRecordRepository(IPlatformPersistenceContext persistenceContext)
    {
        _persistenceContext = persistenceContext;
    }

    public Task<ApiIdempotencyRecord?> FindByScopeAndKeyAsync(
        string scopeTuple,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        return _persistenceContext.ApiIdempotencyRecords
            .FirstOrDefaultAsync(
                x => x.ScopeTuple == scopeTuple && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    public async Task<ApiIdempotencyRecord> AddOrGetExistingAsync(
        ApiIdempotencyRecord record,
        CancellationToken cancellationToken = default)
    {
        var existing = await FindByScopeAndKeyAsync(
            record.ScopeTuple,
            record.IdempotencyKey,
            cancellationToken);

        if (existing != null)
        {
            return existing;
        }

        await _persistenceContext.ApiIdempotencyRecords.AddAsync(record, cancellationToken);
        return record;
    }

    public Task UpdateAsync(
        ApiIdempotencyRecord record,
        CancellationToken cancellationToken = default)
    {
        _persistenceContext.ApiIdempotencyRecords.Update(record);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Counts idempotency records with in-progress state (ResponseStatusCode = 0).
    /// Used for operational visibility of concurrent requests being processed.
    /// </summary>
    public async Task<int> CountInProgressAsync(CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.ApiIdempotencyRecords
            .Where(x => x.ResponseStatusCode < 100)
            .CountAsync(cancellationToken);
    }

    /// <summary>
    /// Counts idempotency records by response status code for operational metrics.
    /// </summary>
    public async Task<int> CountByStatusCodeAsync(int statusCode, CancellationToken cancellationToken = default)
    {
        return await _persistenceContext.ApiIdempotencyRecords
            .Where(x => x.ResponseStatusCode == statusCode)
            .CountAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes idempotency records older than the specified cutoff date.
    /// Bounded cleanup prevents unbounded table growth while preserving recent audit trail.
    /// Returns the count of deleted records.
    /// </summary>
    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default)
    {
        var recordsToDelete = await _persistenceContext.ApiIdempotencyRecords
            .Where(x => x.ProcessedAt < cutoffDate)
            .ToListAsync(cancellationToken);

        foreach (var record in recordsToDelete)
        {
            _persistenceContext.ApiIdempotencyRecords.Remove(record);
        }

        return recordsToDelete.Count;
    }
}
