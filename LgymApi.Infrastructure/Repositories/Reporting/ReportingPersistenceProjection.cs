using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;

namespace LgymApi.Infrastructure.Repositories.Reporting;

internal static class ReportingPersistenceProjection
{
    public static ReportTemplatePersistenceModel Template(ReportTemplate entity)
        => new(
            entity.Id,
            ReportingPersistenceAccountIds.ToReference(entity.TrainerId),
            entity.Name,
            entity.Description,
            entity.CreatedAt,
            entity.IsDeleted,
            entity.Fields
                .OrderBy(field => field.Order)
                .ThenBy(field => field.CreatedAt)
                .Select(field => new ReportTemplateFieldPersistenceModel(
                    field.Id,
                    field.Key,
                    field.Label,
                    field.Type,
                    field.IsRequired,
                    field.Order,
                    field.ModuleConfig,
                    field.CreatedAt))
                .ToList());

    public static ReportRequestPersistenceModel Request(ReportRequest entity)
        => new(
            entity.Id,
            ReportingPersistenceAccountIds.ToReference(entity.TrainerId),
            ReportingPersistenceAccountIds.ToReference(entity.TraineeId),
            entity.TemplateId,
            entity.RecurringReportAssignmentId,
            entity.Status,
            entity.DueAt,
            entity.SubmittedAt,
            entity.Note,
            entity.CreatedAt,
            entity.IsDeleted,
            Template(entity.Template),
            entity.Submission is null
                ? null
                : new ReportSubmissionFeedbackPersistenceModel(
                    entity.Submission.TrainerFeedbackAddedAt,
                    entity.Submission.TrainerFeedbackReadAt));

    public static ReportSubmissionPersistenceModel Submission(ReportSubmission entity)
        => new(
            entity.Id,
            entity.ReportRequestId,
            ReportingPersistenceAccountIds.ToReference(entity.TraineeId),
            entity.PayloadJson,
            entity.TrainerOverallComment,
            entity.TrainerFieldCommentsJson,
            entity.TrainerFeedbackAddedAt,
            entity.TrainerFeedbackReadAt,
            entity.CreatedAt,
            Request(entity.ReportRequest));

    public static RecurringReportAssignmentPersistenceModel Assignment(RecurringReportAssignment entity)
        => new(
            entity.Id,
            ReportingPersistenceAccountIds.ToReference(entity.TrainerId),
            ReportingPersistenceAccountIds.ToReference(entity.TraineeId),
            entity.TemplateId,
            entity.IntervalValue,
            entity.IntervalUnit,
            entity.StartsAt,
            entity.EndsAt,
            entity.IsActive,
            entity.Note,
            entity.CurrentReportRequestId,
            entity.LastRequestCreatedAt,
            entity.NextEligibleAt,
            entity.CreatedAt,
            entity.IsDeleted,
            Template(entity.Template),
            entity.CurrentReportRequest is null ? null : Request(entity.CurrentReportRequest));

    public static ReportPhotoPersistenceModel Photo(Photo entity)
        => new(
            entity.Id,
            entity.StorageKey,
            entity.MimeType,
            entity.SizeBytes,
            entity.Checksum,
            entity.ThumbnailStorageKey,
            entity.ViewType,
            entity.ReportRequestId,
            ReportingPersistenceAccountIds.ToReference(entity.UploaderUserId),
            ReportingPersistenceAccountIds.ToReference(entity.OwnerUserId),
            entity.CreatedAt,
            entity.IsDeleted);

    public static PendingPhotoUpload UploadSession(PhotoUploadSession entity)
        => new(
            entity.Id,
            entity.StorageKey,
            ReportingPersistenceAccountIds.ToReference(entity.InitiatedByUserId),
            ReportingPersistenceAccountIds.ToReference(entity.OwnerUserId),
            entity.ReportRequestId,
            entity.ViewType,
            entity.DeclaredContentType,
            entity.DeclaredSizeBytes,
            entity.CreatedAt,
            entity.ExpiresAtUtc,
            entity.CompletedAtUtc,
            entity.CompletedPhotoId,
            entity.Status,
            entity.FailureReason);
}
