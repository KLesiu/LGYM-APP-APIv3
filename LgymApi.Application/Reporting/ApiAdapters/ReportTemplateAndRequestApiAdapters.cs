using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Reporting.ApiAdapters;

internal sealed class TrainerReportTemplateApiAdapter : ITrainerReportTemplateApiPort
{
    private readonly IReportingService _reportingService;
    public TrainerReportTemplateApiAdapter(IReportingService reportingService) => _reportingService = reportingService;

    public Task<Result<ReportTemplateResult, AppError>> CreateAsync(AuthenticatedAccountContext trainer, CreateReportTemplateCommand command, CancellationToken cancellationToken = default)
        => _reportingService.CreateTemplateAsync(trainer, command, cancellationToken);

    public Task<Result<List<ReportTemplateResult>, AppError>> GetAllAsync(AuthenticatedAccountContext trainer, CancellationToken cancellationToken = default)
        => _reportingService.GetTrainerTemplatesAsync(trainer, cancellationToken);

    public Task<Result<ReportTemplateResult, AppError>> GetAsync(AuthenticatedAccountContext trainer, Id<ReportTemplate> templateId, CancellationToken cancellationToken = default)
        => _reportingService.GetTrainerTemplateAsync(trainer, templateId, cancellationToken);

    public Task<Result<ReportTemplateResult, AppError>> UpdateAsync(AuthenticatedAccountContext trainer, Id<ReportTemplate> templateId, CreateReportTemplateCommand command, CancellationToken cancellationToken = default)
        => _reportingService.UpdateTemplateAsync(trainer, templateId, command, cancellationToken);

    public Task<Result<Unit, AppError>> DeleteAsync(AuthenticatedAccountContext trainer, Id<ReportTemplate> templateId, CancellationToken cancellationToken = default)
        => _reportingService.DeleteTemplateAsync(trainer, templateId, cancellationToken);
}

internal sealed class TrainerReportRequestApiAdapter : ITrainerReportRequestApiPort
{
    private readonly IReportingService _reportingService;
    public TrainerReportRequestApiAdapter(IReportingService reportingService) => _reportingService = reportingService;

    public Task<Result<ReportRequestResult, AppError>> CreateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CreateReportRequestCommand command, CancellationToken cancellationToken = default)
        => _reportingService.CreateReportRequestAsync(trainer, traineeId, command, cancellationToken);

    public Task<Result<List<ReportSubmissionResult>, AppError>> GetSubmissionsAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _reportingService.GetTraineeSubmissionsAsync(trainer, traineeId, cancellationToken);

    public Task<Result<ReportSubmissionResult, AppError>> UpdateFeedbackAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<ReportSubmission> submissionId, UpdateReportSubmissionFeedbackCommand command, CancellationToken cancellationToken = default)
        => _reportingService.UpdateTrainerFeedbackAsync(trainer, traineeId, submissionId, command, cancellationToken);
}

internal sealed class TraineeReportRequestApiAdapter : ITraineeReportRequestApiPort
{
    private readonly IReportingService _reportingService;
    public TraineeReportRequestApiAdapter(IReportingService reportingService) => _reportingService = reportingService;

    public Task<Result<List<ReportRequestResult>, AppError>> GetPendingAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default)
        => _reportingService.GetPendingRequestsForTraineeAsync(trainee, cancellationToken);

    public Task<Result<ReportSubmissionResult, AppError>> SubmitAsync(AuthenticatedAccountContext trainee, Id<ReportRequest> requestId, SubmitReportRequestCommand command, CancellationToken cancellationToken = default)
        => _reportingService.SubmitReportRequestAsync(trainee, requestId, command, cancellationToken);

    public Task<Result<List<ReportSubmissionResult>, AppError>> GetOwnSubmissionsAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default)
        => _reportingService.GetOwnSubmissionsAsync(trainee, cancellationToken);

    public Task<Result<ReportSubmissionResult, AppError>> MarkFeedbackReadAsync(AuthenticatedAccountContext trainee, Id<ReportSubmission> submissionId, CancellationToken cancellationToken = default)
        => _reportingService.MarkTrainerFeedbackAsReadAsync(trainee, submissionId, cancellationToken);
}
