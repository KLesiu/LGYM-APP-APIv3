using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Reporting.Persistence;

public interface IReportRequestSubmissionPersistence
{
    Task AddRequestAsync(NewReportRequestPersistenceModel request, CancellationToken cancellationToken = default);
    Task<ReportRequestPersistenceModel?> FindRequestByIdAsync(Id<ReportRequest> requestId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportRequestPersistenceModel>> ListPendingOrExpiredByTraineeAsync(Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task SetRequestExpiredAsync(Id<ReportRequest> requestId, CancellationToken cancellationToken = default);
    Task SetRequestSubmittedAsync(Id<ReportRequest> requestId, DateTimeOffset submittedAt, CancellationToken cancellationToken = default);
    Task AddSubmissionAsync(NewReportSubmissionPersistenceModel submission, CancellationToken cancellationToken = default);
    Task<ReportSubmissionPersistenceModel?> FindSubmissionForTrainerAsync(Id<ReportSubmission> submissionId, Id<AccountReference> trainerId, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<ReportSubmissionPersistenceModel?> FindSubmissionForTraineeAsync(Id<ReportSubmission> submissionId, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportSubmissionPersistenceModel>> ListSubmissionsByTraineeAsync(Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportSubmissionPersistenceModel>> ListSubmissionsByTrainerAndTraineeAsync(Id<AccountReference> trainerId, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task UpdateFeedbackAsync(Id<ReportSubmission> submissionId, ReportSubmissionFeedbackUpdatePersistenceModel update, CancellationToken cancellationToken = default);
}
