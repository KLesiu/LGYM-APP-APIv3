using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Reporting;

public sealed class ReportPhotoPersistenceRepository : IReportPhotoPersistence
{
    private readonly AppDbContext _dbContext;

    public ReportPhotoPersistenceRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task<ReportPhotoPersistenceModel?> FindByIdAsync(Id<Photo> photoId, CancellationToken cancellationToken = default)
        => FindPhotoAsync(query => query.Where(photo => photo.Id == photoId && !photo.IsDeleted), cancellationToken);

    public Task<ReportPhotoPersistenceModel?> FindActiveByRequestAndViewAsync(
        Id<ReportRequest> requestId,
        string viewType,
        CancellationToken cancellationToken = default)
        => FindPhotoAsync(query => query.Where(photo =>
            photo.ReportRequestId == requestId && photo.ViewType == viewType && !photo.IsDeleted), cancellationToken);

    public Task<IReadOnlyList<ReportPhotoPersistenceModel>> ListByTraineeAsync(
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
    {
        var persistedTraineeId = ReportingPersistenceAccountIds.ToPersisted(traineeId);
        return ListPhotosAsync(
            query => query.Where(photo => photo.OwnerUserId == persistedTraineeId && !photo.IsDeleted)
                .OrderByDescending(photo => photo.CreatedAt),
            cancellationToken);
    }

    public Task<IReadOnlyList<ReportPhotoPersistenceModel>> ListByRequestAsync(
        Id<ReportRequest> requestId,
        CancellationToken cancellationToken = default)
        => ListPhotosAsync(
            query => query.Where(photo => photo.ReportRequestId == requestId && !photo.IsDeleted)
                .OrderBy(photo => photo.ViewType),
            cancellationToken);

    public async Task<IReadOnlyList<ReportPhotoPersistenceModel>> ListByRequestsAsync(
        IReadOnlyCollection<Id<ReportRequest>> requestIds,
        CancellationToken cancellationToken = default)
    {
        if (requestIds.Count == 0)
        {
            return [];
        }

        var entities = await _dbContext.Photos
            .AsNoTracking()
            .Where(photo => requestIds.Contains(photo.ReportRequestId) && !photo.IsDeleted)
            .OrderBy(photo => photo.ReportRequestId)
            .ThenBy(photo => photo.ViewType)
            .ToListAsync(cancellationToken);
        return entities.Select(ReportingPersistenceProjection.Photo).ToList();
    }

    public async Task<long> GetActiveStorageBytesAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Photos
            .AsNoTracking()
            .Where(photo => !photo.IsDeleted)
            .SumAsync(photo => (long?)photo.SizeBytes, cancellationToken) ?? 0L;

    public Task<int> CountCreatedSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default)
        => _dbContext.Photos.IgnoreQueryFilters().CountAsync(photo => photo.CreatedAt >= sinceUtc, cancellationToken);

    public async Task SaveAsync(NewReportPhotoPersistenceModel photo, CancellationToken cancellationToken = default)
    {
        var existingPhoto = await _dbContext.Photos.FirstOrDefaultAsync(candidate =>
            candidate.ReportRequestId == photo.ReportRequestId
            && candidate.ViewType == photo.ViewType
            && !candidate.IsDeleted,
            cancellationToken);
        if (existingPhoto != null)
        {
            existingPhoto.IsDeleted = true;
            existingPhoto.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.Photos.AddAsync(new Photo
        {
            Id = photo.Id,
            StorageKey = photo.StorageKey,
            MimeType = photo.MimeType,
            SizeBytes = photo.SizeBytes,
            Checksum = photo.Checksum,
            ThumbnailStorageKey = photo.ThumbnailStorageKey,
            ViewType = photo.ViewType,
            ReportRequestId = photo.ReportRequestId,
            UploaderUserId = ReportingPersistenceAccountIds.ToPersisted(photo.UploaderAccountId),
            OwnerUserId = ReportingPersistenceAccountIds.ToPersisted(photo.OwnerAccountId),
            CreatedAt = photo.CreatedAt
        }, cancellationToken);
    }

    public Task<int> CountRecentUploadInitsAsync(
        Id<AccountReference> accountId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default)
    {
        var persistedAccountId = ReportingPersistenceAccountIds.ToPersisted(accountId);
        return _dbContext.PhotoUploadSessions.CountAsync(
            session => session.InitiatedByUserId == persistedAccountId && session.CreatedAt >= sinceUtc,
            cancellationToken);
    }

    public Task RecordUploadInitAsync(PendingPhotoUpload pendingUpload, CancellationToken cancellationToken = default)
        => _dbContext.PhotoUploadSessions.AddAsync(new PhotoUploadSession
        {
            Id = pendingUpload.Id.IsEmpty ? Id<PhotoUploadSession>.New() : pendingUpload.Id,
            StorageKey = pendingUpload.StorageKey,
            InitiatedByUserId = ReportingPersistenceAccountIds.ToPersisted(pendingUpload.InitiatedByAccountId),
            OwnerUserId = ReportingPersistenceAccountIds.ToPersisted(pendingUpload.OwnerAccountId),
            ReportRequestId = pendingUpload.ReportRequestId,
            ViewType = pendingUpload.ViewType,
            DeclaredContentType = pendingUpload.DeclaredContentType,
            DeclaredSizeBytes = pendingUpload.DeclaredSizeBytes,
            CreatedAt = pendingUpload.CreatedAtUtc,
            ExpiresAtUtc = pendingUpload.ExpiresAtUtc,
            CompletedAtUtc = pendingUpload.CompletedAtUtc,
            CompletedPhotoId = pendingUpload.CompletedPhotoId,
            Status = pendingUpload.Status,
            FailureReason = pendingUpload.FailureReason
        }, cancellationToken).AsTask();

    public async Task<PendingPhotoUpload?> FindUploadSessionAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.PhotoUploadSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(session => session.StorageKey == storageKey, cancellationToken);
        return entity is null ? null : ReportingPersistenceProjection.UploadSession(entity);
    }

    public Task MarkUploadCompletedAsync(
        string storageKey,
        Id<Photo> photoId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken = default)
        => UpdateUploadSessionAsync(storageKey, session =>
        {
            session.Status = PhotoUploadSessionStatus.Completed;
            session.CompletedPhotoId = photoId;
            session.CompletedAtUtc = completedAtUtc;
            session.FailureReason = null;
        }, cancellationToken);

    public Task MarkUploadFailedAsync(
        string storageKey,
        string failureReason,
        CancellationToken cancellationToken = default)
        => UpdateUploadSessionAsync(storageKey, session =>
        {
            session.Status = PhotoUploadSessionStatus.Failed;
            session.FailureReason = failureReason;
        }, cancellationToken);

    public Task MarkUploadExpiredAsync(string storageKey, CancellationToken cancellationToken = default)
        => UpdateUploadSessionAsync(
            storageKey,
            session => session.Status = PhotoUploadSessionStatus.Expired,
            cancellationToken);

    public async Task<IReadOnlyList<PendingPhotoUpload>> ListCleanupCandidatesAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.PhotoUploadSessions
            .AsNoTracking()
            .Where(session => session.ExpiresAtUtc < nowUtc
                && (session.Status == PhotoUploadSessionStatus.Pending || session.Status == PhotoUploadSessionStatus.Failed))
            .OrderBy(session => session.ExpiresAtUtc)
            .ToListAsync(cancellationToken);
        return entities.Select(ReportingPersistenceProjection.UploadSession).ToList();
    }

    public async Task RemovePendingUploadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.PhotoUploadSessions.FirstOrDefaultAsync(
            session => session.StorageKey == storageKey,
            cancellationToken);
        if (entity != null)
        {
            entity.IsDeleted = true;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task<ReportPhotoPersistenceModel?> FindPhotoAsync(
        Func<IQueryable<Photo>, IQueryable<Photo>> filter,
        CancellationToken cancellationToken)
    {
        var entity = await filter(_dbContext.Photos.AsNoTracking()).FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : ReportingPersistenceProjection.Photo(entity);
    }

    private async Task<IReadOnlyList<ReportPhotoPersistenceModel>> ListPhotosAsync(
        Func<IQueryable<Photo>, IOrderedQueryable<Photo>> queryBuilder,
        CancellationToken cancellationToken)
    {
        var entities = await queryBuilder(_dbContext.Photos.AsNoTracking()).ToListAsync(cancellationToken);
        return entities.Select(ReportingPersistenceProjection.Photo).ToList();
    }

    private async Task UpdateUploadSessionAsync(
        string storageKey,
        Action<PhotoUploadSession> update,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.PhotoUploadSessions.FirstOrDefaultAsync(
            session => session.StorageKey == storageKey,
            cancellationToken);
        if (entity == null)
        {
            return;
        }

        update(entity);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
