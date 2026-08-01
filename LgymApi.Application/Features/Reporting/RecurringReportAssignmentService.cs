using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class RecurringReportAssignmentService : IRecurringReportAssignmentService
{
    private readonly IReportTemplatePersistence _templatePersistence;
    private readonly IReportRequestSubmissionPersistence _requestSubmissionPersistence;
    private readonly IRecurringReportAssignmentPersistence _assignmentPersistence;
    private readonly IReportingRelationshipAccessPersistence _relationshipAccessPersistence;
    private readonly IMapper _mapper;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IUnitOfWork _unitOfWork;

    public RecurringReportAssignmentService(
        IReportTemplatePersistence templatePersistence,
        IReportRequestSubmissionPersistence requestSubmissionPersistence,
        IRecurringReportAssignmentPersistence recurringAssignmentPersistence,
        IReportingRelationshipAccessPersistence relationshipAccessPersistence,
        IMapper mapper,
        ICommandDispatcher commandDispatcher,
        IUnitOfWork unitOfWork)
    {
        _templatePersistence = templatePersistence;
        _requestSubmissionPersistence = requestSubmissionPersistence;
        _assignmentPersistence = recurringAssignmentPersistence;
        _relationshipAccessPersistence = relationshipAccessPersistence;
        _mapper = mapper;
        _commandDispatcher = commandDispatcher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<RecurringReportAssignmentResult, AppError>> CreateAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        UpsertRecurringReportAssignmentCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateTrainerAndCommandAsync(currentTrainer, traineeId, command, cancellationToken);
        if (validation.IsFailure)
        {
            return Result<RecurringReportAssignmentResult, AppError>.Failure(validation.Error);
        }

        var assignment = new NewRecurringReportAssignmentPersistenceModel(
            Id<RecurringReportAssignment>.New(),
            currentTrainer.Id,
            traineeId,
            validation.Value.Id,
            command.IntervalValue,
            command.IntervalUnit,
            command.StartsAt,
            command.EndsAt,
            true,
            NormalizeNote(command.Note),
            null,
            null,
            command.StartsAt,
            DateTimeOffset.UtcNow);

        await _assignmentPersistence.AddAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<RecurringReportAssignmentResult, AppError>.Success(
            MapAssignment(ToPersistenceModel(assignment, validation.Value)));
    }

    public async Task<Result<List<RecurringReportAssignmentResult>, AppError>> GetForTraineeAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
    {
        var ownershipCheck = await EnsureTrainerOwnsTraineeAsync(currentTrainer, traineeId, cancellationToken);
        if (ownershipCheck.IsFailure)
        {
            return Result<List<RecurringReportAssignmentResult>, AppError>.Failure(ownershipCheck.Error);
        }

        var assignments = await _assignmentPersistence.ListByTrainerAndTraineeAsync(
            currentTrainer.Id,
            traineeId,
            cancellationToken);
        return Result<List<RecurringReportAssignmentResult>, AppError>.Success(
            _mapper.MapList<RecurringReportAssignmentPersistenceModel, RecurringReportAssignmentResult>(assignments));
    }

    public async Task<Result<RecurringReportAssignmentResult, AppError>> UpdateAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        Id<RecurringReportAssignment> assignmentId,
        UpsertRecurringReportAssignmentCommand command,
        CancellationToken cancellationToken = default)
    {
        var assignmentResult = await GetOwnedAssignmentAsync(currentTrainer, traineeId, assignmentId, cancellationToken);
        if (assignmentResult.IsFailure)
        {
            return Result<RecurringReportAssignmentResult, AppError>.Failure(assignmentResult.Error);
        }

        var validation = await ValidateTrainerAndCommandAsync(currentTrainer, traineeId, command, cancellationToken);
        if (validation.IsFailure)
        {
            return Result<RecurringReportAssignmentResult, AppError>.Failure(validation.Error);
        }

        var updated = assignmentResult.Value with
        {
            TemplateId = validation.Value.Id,
            Template = validation.Value,
            IntervalValue = command.IntervalValue,
            IntervalUnit = command.IntervalUnit,
            StartsAt = command.StartsAt,
            EndsAt = command.EndsAt,
            Note = NormalizeNote(command.Note)
        };
        updated = updated with { NextEligibleAt = RecalculateNextEligibleAt(updated) };

        await _assignmentPersistence.UpdateAsync(updated.Id, ToUpdateModel(updated), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<RecurringReportAssignmentResult, AppError>.Success(MapAssignment(updated));
    }

    public Task<Result<RecurringReportAssignmentResult, AppError>> PauseAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        Id<RecurringReportAssignment> assignmentId,
        CancellationToken cancellationToken = default)
        => SetActiveAsync(currentTrainer, traineeId, assignmentId, false, cancellationToken);

    public Task<Result<RecurringReportAssignmentResult, AppError>> ResumeAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        Id<RecurringReportAssignment> assignmentId,
        CancellationToken cancellationToken = default)
        => SetActiveAsync(currentTrainer, traineeId, assignmentId, true, cancellationToken);

    public async Task<Result<Unit, AppError>> DeleteAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        Id<RecurringReportAssignment> assignmentId,
        CancellationToken cancellationToken = default)
    {
        var assignmentResult = await GetOwnedAssignmentAsync(currentTrainer, traineeId, assignmentId, cancellationToken);
        if (assignmentResult.IsFailure)
        {
            return Result<Unit, AppError>.Failure(assignmentResult.Error);
        }

        var deleted = assignmentResult.Value with { IsActive = false, IsDeleted = true };
        await _assignmentPersistence.UpdateAsync(deleted.Id, ToUpdateModel(deleted), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task ProcessDueAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var dueAssignments = await _assignmentPersistence.ListDueAsync(now, cancellationToken);

        foreach (var dueAssignment in dueAssignments)
        {
            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
            var assignment = await _assignmentPersistence.FindByIdAsync(dueAssignment.Id, cancellationToken);
            if (assignment == null || !CanCreateNextRequest(assignment, now))
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            if (assignment.Template.IsDeleted)
            {
                var inactive = assignment with { IsActive = false };
                await _assignmentPersistence.UpdateAsync(inactive.Id, ToUpdateModel(inactive), cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var request = new NewReportRequestPersistenceModel(
                Id<ReportRequest>.New(),
                assignment.TrainerId,
                assignment.TraineeId,
                assignment.TemplateId,
                assignment.Id,
                ReportRequestStatus.Pending,
                null,
                null,
                assignment.Note,
                now);
            await _requestSubmissionPersistence.AddRequestAsync(request, cancellationToken);

            var updated = assignment with
            {
                CurrentReportRequestId = request.Id,
                CurrentReportRequest = ToRequestPersistenceModel(request, assignment.Template),
                LastRequestCreatedAt = now,
                NextEligibleAt = null
            };
            await _assignmentPersistence.UpdateAsync(updated.Id, ToUpdateModel(updated), cancellationToken);

            await _commandDispatcher.EnqueueAsync(new ReportRequestCreatedInAppNotificationCommand
            {
                RequestId = request.Id,
                TraineeId = assignment.TraineeId,
                TrainerId = assignment.TrainerId,
                TemplateName = assignment.Template.Name
            });

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private async Task<Result<RecurringReportAssignmentResult, AppError>> SetActiveAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        Id<RecurringReportAssignment> assignmentId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var assignmentResult = await GetOwnedAssignmentAsync(currentTrainer, traineeId, assignmentId, cancellationToken);
        if (assignmentResult.IsFailure)
        {
            return Result<RecurringReportAssignmentResult, AppError>.Failure(assignmentResult.Error);
        }

        var updated = assignmentResult.Value with { IsActive = isActive };
        if (isActive)
        {
            updated = updated with { NextEligibleAt = RecalculateNextEligibleAt(updated) };
        }

        await _assignmentPersistence.UpdateAsync(updated.Id, ToUpdateModel(updated), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<RecurringReportAssignmentResult, AppError>.Success(MapAssignment(updated));
    }
}
