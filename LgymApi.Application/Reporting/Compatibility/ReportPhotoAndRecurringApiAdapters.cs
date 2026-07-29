using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Reporting.Compatibility;

internal sealed class TrainerReportPhotoApiAdapter : ITrainerReportPhotoApiPort
{
    private readonly IReportingService _reportingService;
    private readonly IMapper _mapper;

    public TrainerReportPhotoApiAdapter(IReportingService reportingService, IMapper mapper)
    {
        _reportingService = reportingService;
        _mapper = mapper;
    }

    public Task<Result<InitiatePhotoUploadResult, AppError>> InitiateAsync(AuthenticatedAccountContext actor, InitiatePhotoUploadCommand command, CancellationToken cancellationToken = default)
        => _reportingService.InitiatePhotoUploadAsync(actor, command, cancellationToken);

    public Task<Result<SignedReadUrlResult, AppError>> GetSignedReadUrlAsync(AuthenticatedAccountContext actor, Id<Photo> photoId, CancellationToken cancellationToken = default)
        => _reportingService.GetSignedReadUrlAsync(actor, photoId, cancellationToken);

    public Task<Result<CompletePhotoUploadResult, AppError>> CompleteAsync(AuthenticatedAccountContext actor, CompletePhotoUploadCommand command, CancellationToken cancellationToken = default)
        => _reportingService.CompletePhotoUploadAsync(actor, command, cancellationToken);

    public Task<Result<List<PhotoHistoryItemResult>, AppError>> GetHistoryAsync(AuthenticatedAccountContext actor, Id<AccountReference>? traineeId, Id<ReportRequest>? requestId, CancellationToken cancellationToken = default)
        => _reportingService.GetPhotoHistoryAsync(actor, _mapper.Map<PhotoHistoryAccountInput, GetPhotoHistoryCommand>(new(traineeId, requestId)), cancellationToken);
}

internal sealed class TraineeReportPhotoApiAdapter : ITraineeReportPhotoApiPort
{
    private readonly IReportingService _reportingService;
    private readonly IMapper _mapper;

    public TraineeReportPhotoApiAdapter(IReportingService reportingService, IMapper mapper)
    {
        _reportingService = reportingService;
        _mapper = mapper;
    }

    public Task<Result<InitiatePhotoUploadResult, AppError>> InitiateAsync(AuthenticatedAccountContext actor, InitiatePhotoUploadCommand command, CancellationToken cancellationToken = default)
        => _reportingService.InitiatePhotoUploadAsync(actor, command, cancellationToken);

    public Task<Result<CompletePhotoUploadResult, AppError>> CompleteAsync(AuthenticatedAccountContext actor, CompletePhotoUploadCommand command, CancellationToken cancellationToken = default)
        => _reportingService.CompletePhotoUploadAsync(actor, command, cancellationToken);

    public Task<Result<List<PhotoHistoryItemResult>, AppError>> GetHistoryAsync(AuthenticatedAccountContext actor, Id<ReportRequest>? requestId, CancellationToken cancellationToken = default)
        => _reportingService.GetPhotoHistoryAsync(actor, _mapper.Map<PhotoHistoryAccountInput, GetPhotoHistoryCommand>(new(actor.Id, requestId)), cancellationToken);
}

internal sealed class RecurringReportAssignmentApiAdapter : IRecurringReportAssignmentApiPort
{
    private readonly IRecurringReportAssignmentService _recurringService;
    public RecurringReportAssignmentApiAdapter(IRecurringReportAssignmentService recurringService) => _recurringService = recurringService;

    public Task<Result<RecurringReportAssignmentResult, AppError>> CreateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken = default)
        => _recurringService.CreateAsync(trainer, traineeId, command, cancellationToken);

    public Task<Result<List<RecurringReportAssignmentResult>, AppError>> GetAllAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _recurringService.GetForTraineeAsync(trainer, traineeId, cancellationToken);

    public Task<Result<RecurringReportAssignmentResult, AppError>> UpdateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken = default)
        => _recurringService.UpdateAsync(trainer, traineeId, assignmentId, command, cancellationToken);

    public Task<Result<RecurringReportAssignmentResult, AppError>> PauseAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default)
        => _recurringService.PauseAsync(trainer, traineeId, assignmentId, cancellationToken);

    public Task<Result<RecurringReportAssignmentResult, AppError>> ResumeAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default)
        => _recurringService.ResumeAsync(trainer, traineeId, assignmentId, cancellationToken);

    public Task<Result<Unit, AppError>> DeleteAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default)
        => _recurringService.DeleteAsync(trainer, traineeId, assignmentId, cancellationToken);
}
