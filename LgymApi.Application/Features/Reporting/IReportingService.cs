using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Reporting;

public interface IReportingService
{
    Task<Result<ReportTemplateResult, AppError>> CreateTemplateAsync(UserEntity currentTrainer, CreateReportTemplateCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<ReportTemplateResult>, AppError>> GetTrainerTemplatesAsync(UserEntity currentTrainer, CancellationToken cancellationToken = default);
    Task<Result<ReportTemplateResult, AppError>> GetTrainerTemplateAsync(UserEntity currentTrainer, Id<ReportTemplate> templateId, CancellationToken cancellationToken = default);
    Task<Result<ReportTemplateResult, AppError>> UpdateTemplateAsync(UserEntity currentTrainer, Id<ReportTemplate> templateId, CreateReportTemplateCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteTemplateAsync(UserEntity currentTrainer, Id<ReportTemplate> templateId, CancellationToken cancellationToken = default);

    Task<Result<ReportRequestResult, AppError>> CreateReportRequestAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, CreateReportRequestCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<ReportRequestResult>, AppError>> GetPendingRequestsForTraineeAsync(UserEntity currentTrainee, CancellationToken cancellationToken = default);
    Task<Result<ReportSubmissionResult, AppError>> SubmitReportRequestAsync(UserEntity currentTrainee, Id<ReportRequest> requestId, SubmitReportRequestCommand command, CancellationToken cancellationToken = default);
    Task<Result<ReportSubmissionResult, AppError>> UpdateTrainerFeedbackAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<ReportSubmission> submissionId, UpdateReportSubmissionFeedbackCommand command, CancellationToken cancellationToken = default);
    Task<Result<ReportSubmissionResult, AppError>> MarkTrainerFeedbackAsReadAsync(UserEntity currentTrainee, Id<ReportSubmission> submissionId, CancellationToken cancellationToken = default);
    Task<Result<List<ReportSubmissionResult>, AppError>> GetOwnSubmissionsAsync(UserEntity currentTrainee, CancellationToken cancellationToken = default);
    Task<Result<List<ReportSubmissionResult>, AppError>> GetTraineeSubmissionsAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, CancellationToken cancellationToken = default);

    Task<Result<InitiatePhotoUploadResult, AppError>> InitiatePhotoUploadAsync(UserEntity currentUser, InitiatePhotoUploadCommand command, CancellationToken cancellationToken = default);
    Task<Result<SignedReadUrlResult, AppError>> GetSignedReadUrlAsync(UserEntity currentUser, Id<Photo> photoId, CancellationToken cancellationToken = default);
    Task<Result<CompletePhotoUploadResult, AppError>> CompletePhotoUploadAsync(UserEntity currentUser, CompletePhotoUploadCommand command, CancellationToken cancellationToken = default);
    Task<Result<List<PhotoHistoryItemResult>, AppError>> GetPhotoHistoryAsync(UserEntity currentUser, GetPhotoHistoryCommand command, CancellationToken cancellationToken = default);
}
