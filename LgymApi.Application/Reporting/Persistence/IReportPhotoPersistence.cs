using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Reporting.Persistence;

public interface IReportPhotoPersistence
{
    Task<ReportPhotoPersistenceModel?> FindByIdAsync(Id<Photo> photoId, CancellationToken cancellationToken = default);
    Task<ReportPhotoPersistenceModel?> FindActiveByRequestAndViewAsync(Id<ReportRequest> requestId, string viewType, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportPhotoPersistenceModel>> ListByTraineeAsync(Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportPhotoPersistenceModel>> ListByRequestAsync(Id<ReportRequest> requestId, CancellationToken cancellationToken = default);
    Task<long> GetActiveStorageBytesAsync(CancellationToken cancellationToken = default);
    Task<int> CountCreatedSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
    Task SaveAsync(NewReportPhotoPersistenceModel photo, CancellationToken cancellationToken = default);
    Task<int> CountRecentUploadInitsAsync(Id<AccountReference> accountId, DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);
    Task RecordUploadInitAsync(PendingPhotoUpload pendingUpload, CancellationToken cancellationToken = default);
    Task<PendingPhotoUpload?> FindUploadSessionAsync(string storageKey, CancellationToken cancellationToken = default);
    Task MarkUploadCompletedAsync(string storageKey, Id<Photo> photoId, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default);
    Task MarkUploadFailedAsync(string storageKey, string failureReason, CancellationToken cancellationToken = default);
    Task MarkUploadExpiredAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PendingPhotoUpload>> ListCleanupCandidatesAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task RemovePendingUploadAsync(string storageKey, CancellationToken cancellationToken = default);
}
