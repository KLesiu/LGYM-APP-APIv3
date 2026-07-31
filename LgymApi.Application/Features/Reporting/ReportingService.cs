using System.Text.Json;
using System.Globalization;
using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Security;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class ReportingService : IReportingService
{
    private readonly IReportTemplatePersistence _templatePersistence;
    private readonly IReportRequestSubmissionPersistence _requestSubmissionPersistence;
    private readonly IRecurringReportAssignmentPersistence _recurringAssignmentPersistence;
    private readonly IReportPhotoPersistence _photoPersistence;
    private readonly IReportingRelationshipAccessPersistence _relationshipAccessPersistence;
    private readonly IReportSubmissionAcceptedProgressCommandFactory _reportSubmissionAcceptedProgressCommandFactory;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly ICommandOutboxWriter _commandOutboxWriter;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPhotoStorageProvider _photoStorageProvider;
    private readonly IMapper _mapper;
    private readonly ILogger<ReportingService> _logger;
    private readonly PhotoStorageOptions _photoStorageOptions;

    public ReportingService(
        IReportTemplatePersistence templatePersistence,
        IReportRequestSubmissionPersistence requestSubmissionPersistence,
        IRecurringReportAssignmentPersistence recurringAssignmentPersistence,
        IReportPhotoPersistence photoPersistence,
        IReportingRelationshipAccessPersistence relationshipAccessPersistence,
        IReportSubmissionAcceptedProgressCommandFactory reportSubmissionAcceptedProgressCommandFactory,
        ICommandDispatcher commandDispatcher,
        ICommandOutboxWriter commandOutboxWriter,
        IUnitOfWork unitOfWork,
        IPhotoStorageProvider photoStorageProvider,
        IMapper mapper,
        ILogger<ReportingService> logger,
        PhotoStorageOptions photoStorageOptions)
    {
        _templatePersistence = templatePersistence;
        _requestSubmissionPersistence = requestSubmissionPersistence;
        _recurringAssignmentPersistence = recurringAssignmentPersistence;
        _photoPersistence = photoPersistence;
        _relationshipAccessPersistence = relationshipAccessPersistence;
        _reportSubmissionAcceptedProgressCommandFactory = reportSubmissionAcceptedProgressCommandFactory;
        _commandDispatcher = commandDispatcher;
        _commandOutboxWriter = commandOutboxWriter;
        _unitOfWork = unitOfWork;
        _photoStorageProvider = photoStorageProvider;
        _mapper = mapper;
        _logger = logger;
        _photoStorageOptions = photoStorageOptions;
    }

    private static Result<Unit, AppError> EnsureTrainer(AuthenticatedAccountContext currentTrainer)
    {
        if (!currentTrainer.Roles.Contains(AuthConstants.Roles.Trainer, StringComparer.Ordinal))
        {
            return Result<Unit, AppError>.Failure(new ReportingForbiddenError(Messages.TrainerRoleRequired));
        }

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    private async Task<Result<Unit, AppError>> EnsureTrainerOwnsTraineeAsync(AuthenticatedAccountContext currentTrainer, Id<AccountReference> traineeId, CancellationToken cancellationToken)
    {
        var trainerCheck = EnsureTrainer(currentTrainer);
        if (trainerCheck.IsFailure)
        {
            return Result<Unit, AppError>.Failure(new ReportingForbiddenError(Messages.TrainerRoleRequired));
        }

        if (traineeId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.UserIdRequired));
        }

        var access = await _relationshipAccessPersistence.GetAccessAsync(currentTrainer.Id, traineeId, cancellationToken);
        if (!access.HasActiveRelationship)
        {
            return Result<Unit, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
        }

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    private async Task<Result<ReportTemplatePersistenceModel, AppError>> EnsureOwnedTemplateAsync(AuthenticatedAccountContext currentTrainer, Id<LgymApi.Domain.Entities.ReportTemplate> templateId, CancellationToken cancellationToken)
    {
        if (templateId.IsEmpty)
        {
            return Result<ReportTemplatePersistenceModel, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        var template = await _templatePersistence.FindByIdAsync(templateId, cancellationToken);
        if (template == null || template.TrainerId != currentTrainer.Id || template.IsDeleted)
        {
            return Result<ReportTemplatePersistenceModel, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
        }

        return Result<ReportTemplatePersistenceModel, AppError>.Success(template);
    }
}
