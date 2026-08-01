using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Reporting;

public sealed class ReportRequestSubmissionPersistenceRepository : IReportRequestSubmissionPersistence
{
    private readonly AppDbContext _dbContext;

    public ReportRequestSubmissionPersistenceRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task AddRequestAsync(NewReportRequestPersistenceModel request, CancellationToken cancellationToken = default)
        => _dbContext.ReportRequests.AddAsync(new ReportRequest
        {
            Id = request.Id,
            TrainerId = ReportingPersistenceAccountIds.ToPersisted(request.TrainerId),
            TraineeId = ReportingPersistenceAccountIds.ToPersisted(request.TraineeId),
            TemplateId = request.TemplateId,
            RecurringReportAssignmentId = request.RecurringReportAssignmentId,
            Status = request.Status,
            DueAt = request.DueAt,
            SubmittedAt = request.SubmittedAt,
            Note = request.Note,
            CreatedAt = request.CreatedAt
        }, cancellationToken).AsTask();

    public async Task<ReportRequestPersistenceModel?> FindRequestByIdAsync(
        Id<ReportRequest> requestId,
        CancellationToken cancellationToken = default)
    {
        var entity = await RequestQuery().AsNoTracking().FirstOrDefaultAsync(request => request.Id == requestId, cancellationToken);
        return entity is null ? null : ReportingPersistenceProjection.Request(entity);
    }

    public async Task<IReadOnlyList<ReportRequestPersistenceModel>> ListPendingOrExpiredByTraineeAsync(
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
    {
        var persistedTraineeId = ReportingPersistenceAccountIds.ToPersisted(traineeId);
        var entities = await RequestQuery()
            .AsNoTracking()
            .Where(request => request.TraineeId == persistedTraineeId
                && (request.Status == ReportRequestStatus.Pending || request.Status == ReportRequestStatus.Expired))
            .OrderByDescending(request => request.CreatedAt)
            .ToListAsync(cancellationToken);
        return entities.Select(ReportingPersistenceProjection.Request).ToList();
    }

    public Task SetRequestExpiredAsync(Id<ReportRequest> requestId, CancellationToken cancellationToken = default)
        => SetRequestStateAsync(requestId, ReportRequestStatus.Expired, null, cancellationToken);

    public Task SetRequestSubmittedAsync(
        Id<ReportRequest> requestId,
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken = default)
        => SetRequestStateAsync(requestId, ReportRequestStatus.Submitted, submittedAt, cancellationToken);

    public Task AddSubmissionAsync(NewReportSubmissionPersistenceModel submission, CancellationToken cancellationToken = default)
        => _dbContext.ReportSubmissions.AddAsync(new ReportSubmission
        {
            Id = submission.Id,
            ReportRequestId = submission.ReportRequestId,
            TraineeId = ReportingPersistenceAccountIds.ToPersisted(submission.TraineeId),
            PayloadJson = submission.PayloadJson,
            CreatedAt = submission.CreatedAt
        }, cancellationToken).AsTask();

    public Task<ReportSubmissionPersistenceModel?> FindSubmissionForTrainerAsync(
        Id<ReportSubmission> submissionId,
        Id<AccountReference> trainerId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
        => FindSubmissionAsync(query => query.Where(submission =>
            submission.Id == submissionId
            && submission.TraineeId == ReportingPersistenceAccountIds.ToPersisted(traineeId)
            && submission.ReportRequest.TrainerId == ReportingPersistenceAccountIds.ToPersisted(trainerId)), cancellationToken);

    public Task<ReportSubmissionPersistenceModel?> FindSubmissionForTraineeAsync(
        Id<ReportSubmission> submissionId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
        => FindSubmissionAsync(query => query.Where(submission =>
            submission.Id == submissionId
            && submission.TraineeId == ReportingPersistenceAccountIds.ToPersisted(traineeId)), cancellationToken);

    public Task<IReadOnlyList<ReportSubmissionPersistenceModel>> ListSubmissionsByTraineeAsync(
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
        => ListSubmissionsAsync(query => query.Where(submission =>
            submission.TraineeId == ReportingPersistenceAccountIds.ToPersisted(traineeId)), cancellationToken);

    public Task<IReadOnlyList<ReportSubmissionPersistenceModel>> ListSubmissionsByTrainerAndTraineeAsync(
        Id<AccountReference> trainerId,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
        => ListSubmissionsAsync(query => query.Where(submission =>
            submission.ReportRequest.TrainerId == ReportingPersistenceAccountIds.ToPersisted(trainerId)
            && submission.TraineeId == ReportingPersistenceAccountIds.ToPersisted(traineeId)), cancellationToken);

    public async Task UpdateFeedbackAsync(
        Id<ReportSubmission> submissionId,
        ReportSubmissionFeedbackUpdatePersistenceModel update,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ReportSubmissions.FirstAsync(submission => submission.Id == submissionId, cancellationToken);
        entity.TrainerOverallComment = update.TrainerOverallComment;
        entity.TrainerFieldCommentsJson = update.TrainerFieldCommentsJson;
        entity.TrainerFeedbackAddedAt = update.TrainerFeedbackAddedAt;
        entity.TrainerFeedbackReadAt = update.TrainerFeedbackReadAt;
    }

    private IQueryable<ReportRequest> RequestQuery()
        => _dbContext.ReportRequests
            .Include(request => request.Template)
                .ThenInclude(template => template.Fields.OrderBy(field => field.Order).ThenBy(field => field.CreatedAt))
            .Include(request => request.Submission);

    private IQueryable<ReportSubmission> SubmissionQuery()
        => _dbContext.ReportSubmissions
            .Include(submission => submission.ReportRequest)
                .ThenInclude(request => request.Template)
                    .ThenInclude(template => template.Fields.OrderBy(field => field.Order).ThenBy(field => field.CreatedAt));

    private async Task SetRequestStateAsync(
        Id<ReportRequest> requestId,
        ReportRequestStatus status,
        DateTimeOffset? submittedAt,
        CancellationToken cancellationToken)
    {
        var entity = await _dbContext.ReportRequests.FirstAsync(request => request.Id == requestId, cancellationToken);
        entity.Status = status;
        if (submittedAt.HasValue)
        {
            entity.SubmittedAt = submittedAt;
        }
    }

    private async Task<ReportSubmissionPersistenceModel?> FindSubmissionAsync(
        Func<IQueryable<ReportSubmission>, IQueryable<ReportSubmission>> filter,
        CancellationToken cancellationToken)
    {
        var entity = await filter(SubmissionQuery()).AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : ReportingPersistenceProjection.Submission(entity);
    }

    private async Task<IReadOnlyList<ReportSubmissionPersistenceModel>> ListSubmissionsAsync(
        Func<IQueryable<ReportSubmission>, IQueryable<ReportSubmission>> filter,
        CancellationToken cancellationToken)
    {
        var entities = await filter(SubmissionQuery())
            .AsNoTracking()
            .OrderByDescending(submission => submission.CreatedAt)
            .ToListAsync(cancellationToken);
        return entities.Select(ReportingPersistenceProjection.Submission).ToList();
    }
}
