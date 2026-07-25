using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class RecurringReportAssignmentService : IRecurringReportAssignmentService
{
    private readonly ICoachingRelationshipAccessService _coachingRelationshipAccessService;
    private readonly IReportingRepository _reportingRepository;
    private readonly IRecurringReportAssignmentRepository _assignmentRepository;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IUnitOfWork _unitOfWork;

    public RecurringReportAssignmentService(IRecurringReportAssignmentServiceDependencies dependencies)
    {
        _coachingRelationshipAccessService = dependencies.CoachingRelationshipAccessService;
        _reportingRepository = dependencies.ReportingRepository;
        _assignmentRepository = dependencies.RecurringReportAssignmentRepository;
        _commandDispatcher = dependencies.CommandDispatcher;
        _unitOfWork = dependencies.UnitOfWork;
    }

    public async Task<Result<RecurringReportAssignmentResult, AppError>> CreateAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken = default)
    {
        var validation = await ValidateTrainerAndCommandAsync(currentTrainer, traineeId, command, cancellationToken);
        if (validation.IsFailure)
        {
            return Result<RecurringReportAssignmentResult, AppError>.Failure(validation.Error);
        }

        var template = validation.Value;
        var assignment = new RecurringReportAssignment
        {
            Id = Id<RecurringReportAssignment>.New(),
            TrainerId = currentTrainer.Id,
            TraineeId = traineeId,
            TemplateId = template.Id,
            IntervalValue = command.IntervalValue,
            IntervalUnit = command.IntervalUnit,
            StartsAt = command.StartsAt,
            EndsAt = command.EndsAt,
            IsActive = true,
            Note = NormalizeNote(command.Note),
            NextEligibleAt = command.StartsAt
        };

        await _assignmentRepository.AddAsync(assignment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        assignment.Template = template;

        return Result<RecurringReportAssignmentResult, AppError>.Success(MapAssignment(assignment));
    }

    public async Task<Result<List<RecurringReportAssignmentResult>, AppError>> GetForTraineeAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, CancellationToken cancellationToken = default)
    {
        var ownershipCheck = await EnsureTrainerOwnsTraineeAsync(currentTrainer, traineeId, cancellationToken);
        if (ownershipCheck.IsFailure)
        {
            return Result<List<RecurringReportAssignmentResult>, AppError>.Failure(ownershipCheck.Error);
        }

        var assignments = await _assignmentRepository.GetByTrainerAndTraineeAsync(currentTrainer.Id, traineeId, cancellationToken);
        return Result<List<RecurringReportAssignmentResult>, AppError>.Success(assignments.Select(MapAssignment).ToList());
    }

    public async Task<Result<RecurringReportAssignmentResult, AppError>> UpdateAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<RecurringReportAssignment> assignmentId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken = default)
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

        var assignment = assignmentResult.Value;
        assignment.TemplateId = validation.Value.Id;
        assignment.Template = validation.Value;
        assignment.IntervalValue = command.IntervalValue;
        assignment.IntervalUnit = command.IntervalUnit;
        assignment.StartsAt = command.StartsAt;
        assignment.EndsAt = command.EndsAt;
        assignment.Note = NormalizeNote(command.Note);
        assignment.NextEligibleAt = RecalculateNextEligibleAt(assignment);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<RecurringReportAssignmentResult, AppError>.Success(MapAssignment(assignment));
    }

    public async Task<Result<RecurringReportAssignmentResult, AppError>> PauseAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default)
    {
        var assignmentResult = await GetOwnedAssignmentAsync(currentTrainer, traineeId, assignmentId, cancellationToken);
        if (assignmentResult.IsFailure)
        {
            return Result<RecurringReportAssignmentResult, AppError>.Failure(assignmentResult.Error);
        }

        assignmentResult.Value.IsActive = false;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<RecurringReportAssignmentResult, AppError>.Success(MapAssignment(assignmentResult.Value));
    }

    public async Task<Result<RecurringReportAssignmentResult, AppError>> ResumeAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default)
    {
        var assignmentResult = await GetOwnedAssignmentAsync(currentTrainer, traineeId, assignmentId, cancellationToken);
        if (assignmentResult.IsFailure)
        {
            return Result<RecurringReportAssignmentResult, AppError>.Failure(assignmentResult.Error);
        }

        var assignment = assignmentResult.Value;
        assignment.IsActive = true;
        assignment.NextEligibleAt = RecalculateNextEligibleAt(assignment);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<RecurringReportAssignmentResult, AppError>.Success(MapAssignment(assignment));
    }

    public async Task<Result<Unit, AppError>> DeleteAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken = default)
    {
        var assignmentResult = await GetOwnedAssignmentAsync(currentTrainer, traineeId, assignmentId, cancellationToken);
        if (assignmentResult.IsFailure)
        {
            return Result<Unit, AppError>.Failure(assignmentResult.Error);
        }

        assignmentResult.Value.IsActive = false;
        assignmentResult.Value.IsDeleted = true;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task ProcessDueAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var dueAssignments = await _assignmentRepository.GetDueAssignmentsAsync(now, cancellationToken);

        foreach (var dueAssignment in dueAssignments)
        {
            await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
            var assignment = await _assignmentRepository.FindByIdAsync(dueAssignment.Id, cancellationToken);
            if (assignment == null || !CanCreateNextRequest(assignment, now))
            {
                await transaction.RollbackAsync(cancellationToken);
                continue;
            }

            var template = assignment.Template;
            if (template.IsDeleted)
            {
                assignment.IsActive = false;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var request = new ReportRequest
            {
                Id = Id<ReportRequest>.New(),
                TrainerId = assignment.TrainerId,
                TraineeId = assignment.TraineeId,
                TemplateId = assignment.TemplateId,
                RecurringReportAssignmentId = assignment.Id,
                Status = ReportRequestStatus.Pending,
                Note = assignment.Note
            };

            await _reportingRepository.AddRequestAsync(request, cancellationToken);
            assignment.CurrentReportRequestId = request.Id;
            assignment.CurrentReportRequest = request;
            assignment.LastRequestCreatedAt = now;
            assignment.NextEligibleAt = null;

            await _commandDispatcher.EnqueueAsync(new ReportRequestCreatedInAppNotificationCommand
            {
                RequestId = request.Id,
                TraineeId = assignment.TraineeId,
                TrainerId = assignment.TrainerId,
                TemplateName = template.Name
            });

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

}
