using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Features.Reporting;

public interface IReportingService
{
    Task<Result<ReportTemplateResult, AppError>> CreateTemplateAsync(AuthenticatedAccountContext currentTrainer, CreateReportTemplateCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<ReportTemplateResult>, AppError>> GetTrainerTemplatesAsync(AuthenticatedAccountContext currentTrainer, CancellationToken cancellationToken = default);
    Task<Result<ReportTemplateResult, AppError>> GetTrainerTemplateAsync(AuthenticatedAccountContext currentTrainer, Id<ReportTemplate> templateId, CancellationToken cancellationToken = default);
    Task<Result<ReportTemplateResult, AppError>> UpdateTemplateAsync(AuthenticatedAccountContext currentTrainer, Id<ReportTemplate> templateId, CreateReportTemplateCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteTemplateAsync(AuthenticatedAccountContext currentTrainer, Id<ReportTemplate> templateId, CancellationToken cancellationToken = default);

    Task<Result<ReportRequestResult, AppError>> CreateReportRequestAsync(AuthenticatedAccountContext currentTrainer, Id<AccountReference> traineeId, CreateReportRequestCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<ReportRequestResult>, AppError>> GetPendingRequestsForTraineeAsync(AuthenticatedAccountContext currentTrainee, CancellationToken cancellationToken = default);
    Task<Result<ReportSubmissionResult, AppError>> SubmitReportRequestAsync(AuthenticatedAccountContext currentTrainee, Id<ReportRequest> requestId, SubmitReportRequestCommand command, CancellationToken cancellationToken = default);
    Task<Result<ReportSubmissionResult, AppError>> UpdateTrainerFeedbackAsync(AuthenticatedAccountContext currentTrainer, Id<AccountReference> traineeId, Id<ReportSubmission> submissionId, UpdateReportSubmissionFeedbackCommand command, CancellationToken cancellationToken = default);
    Task<Result<ReportSubmissionResult, AppError>> MarkTrainerFeedbackAsReadAsync(AuthenticatedAccountContext currentTrainee, Id<ReportSubmission> submissionId, CancellationToken cancellationToken = default);
    Task<Result<List<ReportSubmissionResult>, AppError>> GetOwnSubmissionsAsync(AuthenticatedAccountContext currentTrainee, CancellationToken cancellationToken = default);
    Task<Result<List<ReportSubmissionResult>, AppError>> GetTraineeSubmissionsAsync(AuthenticatedAccountContext currentTrainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default);

    Task<Result<InitiatePhotoUploadResult, AppError>> InitiatePhotoUploadAsync(AuthenticatedAccountContext currentUser, InitiatePhotoUploadCommand command, CancellationToken cancellationToken = default);
    Task<Result<SignedReadUrlResult, AppError>> GetSignedReadUrlAsync(AuthenticatedAccountContext currentUser, Id<Photo> photoId, CancellationToken cancellationToken = default);
    Task<Result<CompletePhotoUploadResult, AppError>> CompletePhotoUploadAsync(AuthenticatedAccountContext currentUser, CompletePhotoUploadCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<PhotoHistoryItemResult>, AppError>> GetPhotoHistoryAsync(AuthenticatedAccountContext currentUser, GetPhotoHistoryCommand command, CancellationToken cancellationToken = default);
}
