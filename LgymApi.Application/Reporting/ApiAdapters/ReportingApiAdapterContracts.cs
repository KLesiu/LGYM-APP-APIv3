using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Reporting.ApiAdapters;

public interface ITrainerReportTemplateApiPort
{
    Task<Result<ReportTemplateResult, AppError>> CreateAsync(AuthenticatedAccountContext trainer, CreateReportTemplateCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<ReportTemplateResult>, AppError>> GetAllAsync(AuthenticatedAccountContext trainer, CancellationToken cancellationToken = default);
    Task<Result<ReportTemplateResult, AppError>> GetAsync(AuthenticatedAccountContext trainer, Id<ReportTemplate> templateId, CancellationToken cancellationToken = default);
    Task<Result<ReportTemplateResult, AppError>> UpdateAsync(AuthenticatedAccountContext trainer, Id<ReportTemplate> templateId, CreateReportTemplateCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteAsync(AuthenticatedAccountContext trainer, Id<ReportTemplate> templateId, CancellationToken cancellationToken = default);
}

public interface ITrainerReportRequestApiPort
{
    Task<Result<ReportRequestResult, AppError>> CreateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CreateReportRequestCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<ReportSubmissionResult>, AppError>> GetSubmissionsAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<Result<ReportSubmissionResult, AppError>> UpdateFeedbackAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<ReportSubmission> submissionId, UpdateReportSubmissionFeedbackCommand command, CancellationToken cancellationToken = default);
}

public interface ITraineeReportRequestApiPort
{
    Task<Result<List<ReportRequestResult>, AppError>> GetPendingAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default);
    Task<Result<ReportSubmissionResult, AppError>> SubmitAsync(AuthenticatedAccountContext trainee, Id<ReportRequest> requestId, SubmitReportRequestCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<ReportSubmissionResult>, AppError>> GetOwnSubmissionsAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default);
    Task<Result<ReportSubmissionResult, AppError>> MarkFeedbackReadAsync(AuthenticatedAccountContext trainee, Id<ReportSubmission> submissionId, CancellationToken cancellationToken = default);
}

public interface ITrainerReportPhotoApiPort
{
    Task<Result<InitiatePhotoUploadResult, AppError>> InitiateAsync(AuthenticatedAccountContext actor, InitiatePhotoUploadCommand command, CancellationToken cancellationToken = default);
    Task<Result<SignedReadUrlResult, AppError>> GetSignedReadUrlAsync(AuthenticatedAccountContext actor, Id<Photo> photoId, CancellationToken cancellationToken = default);
    Task<Result<CompletePhotoUploadResult, AppError>> CompleteAsync(AuthenticatedAccountContext actor, CompletePhotoUploadCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<PhotoHistoryItemResult>, AppError>> GetHistoryAsync(AuthenticatedAccountContext actor, Id<AccountReference>? traineeId, Id<ReportRequest>? requestId, CancellationToken cancellationToken = default);
}

public interface ITraineeReportPhotoApiPort
{
    Task<Result<InitiatePhotoUploadResult, AppError>> InitiateAsync(AuthenticatedAccountContext actor, InitiatePhotoUploadCommand command, CancellationToken cancellationToken = default);
    Task<Result<CompletePhotoUploadResult, AppError>> CompleteAsync(AuthenticatedAccountContext actor, CompletePhotoUploadCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<PhotoHistoryItemResult>, AppError>> GetHistoryAsync(AuthenticatedAccountContext actor, Id<ReportRequest>? requestId, CancellationToken cancellationToken = default);
}

public interface IRecurringReportAssignmentApiPort
{
    Task<Result<RecurringReportAssignmentResult, AppError>> CreateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<RecurringReportAssignmentResult>, AppError>> GetAllAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);
    Task<Result<RecurringReportAssignmentResult, AppError>> UpdateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken = default);
    Task<Result<RecurringReportAssignmentResult, AppError>> PauseAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task<Result<RecurringReportAssignmentResult, AppError>> ResumeAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task<Result<RecurringReportAssignmentResult, AppError>> RequestNowAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default);
}
