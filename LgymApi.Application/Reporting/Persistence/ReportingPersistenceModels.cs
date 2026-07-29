using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Reporting.Persistence;

public sealed record ReportTemplateFieldPersistenceModel(
    Id<ReportTemplateField> Id,
    string Key,
    string Label,
    ReportFieldType Type,
    bool IsRequired,
    int Order,
    string? ModuleConfig,
    DateTimeOffset CreatedAt);

public sealed record ReportTemplatePersistenceModel(
    Id<ReportTemplate> Id,
    Id<AccountReference> TrainerId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    bool IsDeleted,
    IReadOnlyList<ReportTemplateFieldPersistenceModel> Fields);

public sealed record NewReportTemplatePersistenceModel(
    Id<ReportTemplate> Id,
    Id<AccountReference> TrainerId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReportTemplateFieldPersistenceModel> Fields);

public sealed record UpdateReportTemplatePersistenceModel(
    string Name,
    string? Description,
    IReadOnlyList<ReportTemplateFieldPersistenceModel> Fields);

public sealed record ReportSubmissionFeedbackPersistenceModel(
    DateTimeOffset? TrainerFeedbackAddedAt,
    DateTimeOffset? TrainerFeedbackReadAt);

public sealed record ReportRequestPersistenceModel(
    Id<ReportRequest> Id,
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    Id<ReportTemplate> TemplateId,
    Id<RecurringReportAssignment>? RecurringReportAssignmentId,
    ReportRequestStatus Status,
    DateTimeOffset? DueAt,
    DateTimeOffset? SubmittedAt,
    string? Note,
    DateTimeOffset CreatedAt,
    bool IsDeleted,
    ReportTemplatePersistenceModel Template,
    ReportSubmissionFeedbackPersistenceModel? Submission);

public sealed record NewReportRequestPersistenceModel(
    Id<ReportRequest> Id,
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    Id<ReportTemplate> TemplateId,
    Id<RecurringReportAssignment>? RecurringReportAssignmentId,
    ReportRequestStatus Status,
    DateTimeOffset? DueAt,
    DateTimeOffset? SubmittedAt,
    string? Note,
    DateTimeOffset CreatedAt);

public sealed record ReportSubmissionPersistenceModel(
    Id<ReportSubmission> Id,
    Id<ReportRequest> ReportRequestId,
    Id<AccountReference> TraineeId,
    string PayloadJson,
    string? TrainerOverallComment,
    string? TrainerFieldCommentsJson,
    DateTimeOffset? TrainerFeedbackAddedAt,
    DateTimeOffset? TrainerFeedbackReadAt,
    DateTimeOffset CreatedAt,
    ReportRequestPersistenceModel ReportRequest);

public sealed record NewReportSubmissionPersistenceModel(
    Id<ReportSubmission> Id,
    Id<ReportRequest> ReportRequestId,
    Id<AccountReference> TraineeId,
    string PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record ReportSubmissionFeedbackUpdatePersistenceModel(
    string? TrainerOverallComment,
    string? TrainerFieldCommentsJson,
    DateTimeOffset? TrainerFeedbackAddedAt,
    DateTimeOffset? TrainerFeedbackReadAt);

public sealed record RecurringReportAssignmentPersistenceModel(
    Id<RecurringReportAssignment> Id,
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    Id<ReportTemplate> TemplateId,
    int IntervalValue,
    RecurringReportIntervalUnit IntervalUnit,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive,
    string? Note,
    Id<ReportRequest>? CurrentReportRequestId,
    DateTimeOffset? LastRequestCreatedAt,
    DateTimeOffset? NextEligibleAt,
    DateTimeOffset CreatedAt,
    bool IsDeleted,
    ReportTemplatePersistenceModel Template,
    ReportRequestPersistenceModel? CurrentReportRequest);

public sealed record NewRecurringReportAssignmentPersistenceModel(
    Id<RecurringReportAssignment> Id,
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    Id<ReportTemplate> TemplateId,
    int IntervalValue,
    RecurringReportIntervalUnit IntervalUnit,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive,
    string? Note,
    Id<ReportRequest>? CurrentReportRequestId,
    DateTimeOffset? LastRequestCreatedAt,
    DateTimeOffset? NextEligibleAt,
    DateTimeOffset CreatedAt);

public sealed record RecurringReportAssignmentUpdatePersistenceModel(
    Id<ReportTemplate> TemplateId,
    int IntervalValue,
    RecurringReportIntervalUnit IntervalUnit,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool IsActive,
    string? Note,
    Id<ReportRequest>? CurrentReportRequestId,
    DateTimeOffset? LastRequestCreatedAt,
    DateTimeOffset? NextEligibleAt,
    bool IsDeleted);

public sealed record ReportPhotoPersistenceModel(
    Id<Photo> Id,
    string StorageKey,
    string MimeType,
    long SizeBytes,
    string Checksum,
    string? ThumbnailStorageKey,
    string ViewType,
    Id<ReportRequest> ReportRequestId,
    Id<AccountReference> UploaderAccountId,
    Id<AccountReference> OwnerAccountId,
    DateTimeOffset CreatedAt,
    bool IsDeleted);

public sealed record NewReportPhotoPersistenceModel(
    Id<Photo> Id,
    string StorageKey,
    string MimeType,
    long SizeBytes,
    string Checksum,
    string? ThumbnailStorageKey,
    string ViewType,
    Id<ReportRequest> ReportRequestId,
    Id<AccountReference> UploaderAccountId,
    Id<AccountReference> OwnerAccountId,
    DateTimeOffset CreatedAt);

public sealed record PendingPhotoUpload(
    Id<PhotoUploadSession> Id,
    string StorageKey,
    Id<AccountReference> InitiatedByAccountId,
    Id<AccountReference> OwnerAccountId,
    Id<ReportRequest> ReportRequestId,
    string ViewType,
    string DeclaredContentType,
    long DeclaredSizeBytes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? CompletedAtUtc,
    Id<Photo>? CompletedPhotoId,
    PhotoUploadSessionStatus Status,
    string? FailureReason);

public sealed record ReportingRelationshipAccessFact(bool HasActiveRelationship);
