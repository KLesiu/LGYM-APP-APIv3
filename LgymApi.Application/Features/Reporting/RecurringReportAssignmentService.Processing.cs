using System.Runtime.ExceptionServices;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class RecurringReportAssignmentService
{
    public async Task ProcessDueAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await _assignmentPersistence.ListDueAsync(now, cancellationToken);

        foreach (var candidate in candidates)
        {
            await ProcessCandidateAsync(candidate.Id, now, cancellationToken);
        }
    }

    private async Task ProcessCandidateAsync(
        Id<RecurringReportAssignment> assignmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        IUnitOfWorkTransaction transaction;
        try
        {
            transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            LogAborted(assignmentId, ProcessingReason.BeginTransactionFailed, exception.GetType().Name);
            throw;
        }

        var phase = TransactionPhase.PreCommit;
        ProcessingResult? result = null;
        ExceptionDispatchInfo? abort = null;
        var preCommitReason = ProcessingReason.PreCommitFailed;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assignment = await _assignmentPersistence.FindByIdForUpdateAsync(assignmentId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (assignment == null)
            {
                var rollback = await RollbackBusinessDecisionAsync(
                    transaction,
                    assignmentId,
                    AutomaticEligibilityReason.AssignmentMissing,
                    cancellationToken);
                result = rollback.Result;
                abort = rollback.Failure == null ? null : ExceptionDispatchInfo.Capture(rollback.Failure);
                phase = rollback.Failure == null
                    ? TransactionPhase.RolledBack
                    : TransactionPhase.RollbackFailed;
            }
            else
            {
                var eligibility = EvaluateAutomaticEligibility(assignment, now);
                if (!eligibility.CanCreate)
                {
                    var rollback = await RollbackBusinessDecisionAsync(
                        transaction,
                        assignmentId,
                        eligibility.Reason,
                        cancellationToken);
                    result = rollback.Result;
                    abort = rollback.Failure == null ? null : ExceptionDispatchInfo.Capture(rollback.Failure);
                    phase = rollback.Failure == null
                        ? TransactionPhase.RolledBack
                        : TransactionPhase.RollbackFailed;
                }
                else if (assignment.Template.IsDeleted)
                {
                    var inactive = assignment with { IsActive = false };
                    await _assignmentPersistence.UpdateAsync(inactive.Id, ToUpdateModel(inactive), cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    phase = TransactionPhase.CommitStarted;
                    await transaction.CommitAsync(cancellationToken);
                    phase = TransactionPhase.Committed;
                    result = ProcessingResult.Deactivated(assignmentId);
                }
                else
                {
                    var staged = await StageRequestCreationAsync(assignment, now, cancellationToken);
                    if (staged.EnvelopeId == null)
                    {
                        preCommitReason = ProcessingReason.EnvelopeMissing;
                        throw new InvalidOperationException("The recurring report notification envelope was not staged.");
                    }

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    phase = TransactionPhase.CommitStarted;
                    await transaction.CommitAsync(cancellationToken);
                    phase = TransactionPhase.Committed;
                    result = ProcessingResult.Created(assignmentId, staged.RequestId, staged.EnvelopeId);
                }
            }
        }
        catch (OperationCanceledException exception)
            when (phase == TransactionPhase.PreCommit && cancellationToken.IsCancellationRequested)
        {
            var rollbackFailure = await TryRollbackAsync(transaction);
            if (rollbackFailure == null)
            {
                phase = TransactionPhase.RolledBack;
                result = ProcessingResult.Aborted(
                    assignmentId,
                    ProcessingReason.CancellationRequested,
                    exception.GetType().Name);
                abort = ExceptionDispatchInfo.Capture(exception);
            }
            else
            {
                phase = TransactionPhase.RollbackFailed;
                result = ProcessingResult.Aborted(
                    assignmentId,
                    ProcessingReason.RollbackFailed,
                    rollbackFailure.GetType().Name);
                abort = ExceptionDispatchInfo.Capture(rollbackFailure);
            }
        }
        catch (Exception exception) when (phase == TransactionPhase.PreCommit)
        {
            var rollbackFailure = await TryRollbackAsync(transaction);
            if (rollbackFailure == null)
            {
                phase = TransactionPhase.RolledBack;
                result = ProcessingResult.Failed(assignmentId, preCommitReason, exception.GetType().Name);
            }
            else
            {
                phase = TransactionPhase.RollbackFailed;
                result = ProcessingResult.Aborted(
                    assignmentId,
                    ProcessingReason.RollbackFailed,
                    rollbackFailure.GetType().Name);
                abort = ExceptionDispatchInfo.Capture(rollbackFailure);
            }
        }
        catch (Exception exception) when (phase == TransactionPhase.CommitStarted)
        {
            result = ProcessingResult.Aborted(
                assignmentId,
                ProcessingReason.CommitFailed,
                exception.GetType().Name);
            abort = ExceptionDispatchInfo.Capture(exception);
        }

        try
        {
            await transaction.DisposeAsync();
        }
        catch (Exception exception)
        {
            LogAborted(assignmentId, ProcessingReason.DisposeFailed, exception.GetType().Name);
            throw;
        }

        LogResult(result!);
        abort?.Throw();
    }

    private async Task<StagedRequestCreation> StageRequestCreationAsync(
        RecurringReportAssignmentPersistenceModel assignment,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
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
            createdAt);
        await _requestSubmissionPersistence.AddRequestAsync(request, cancellationToken);

        var updated = assignment with
        {
            CurrentReportRequestId = request.Id,
            CurrentReportRequest = ToRequestPersistenceModel(request, assignment.Template),
            LastRequestCreatedAt = createdAt,
            NextEligibleAt = null
        };
        await _assignmentPersistence.UpdateAsync(updated.Id, ToUpdateModel(updated), cancellationToken);

        var envelope = await _commandOutboxWriter.StageAsync(new ReportRequestCreatedInAppNotificationCommand
        {
            RequestId = request.Id,
            TraineeId = assignment.TraineeId,
            TrainerId = assignment.TrainerId,
            TemplateName = assignment.Template.Name
        }, cancellationToken);
        return new StagedRequestCreation(request.Id, envelope.EnvelopeId);
    }

    private async Task<BusinessRollbackResult> RollbackBusinessDecisionAsync(
        IUnitOfWorkTransaction transaction,
        Id<RecurringReportAssignment> assignmentId,
        AutomaticEligibilityReason reason,
        CancellationToken cancellationToken)
    {
        var rollbackFailure = await TryRollbackAsync(transaction);
        if (rollbackFailure == null && cancellationToken.IsCancellationRequested)
        {
            var cancellation = new OperationCanceledException(cancellationToken);
            return new BusinessRollbackResult(
                ProcessingResult.Aborted(
                    assignmentId,
                    ProcessingReason.CancellationRequested,
                    cancellation.GetType().Name),
                cancellation);
        }

        return rollbackFailure == null
            ? new BusinessRollbackResult(ProcessingResult.Skipped(assignmentId, reason), null)
            : new BusinessRollbackResult(
                ProcessingResult.Aborted(
                    assignmentId,
                    ProcessingReason.RollbackFailed,
                    rollbackFailure.GetType().Name),
                rollbackFailure);
    }

    private static async Task<Exception?> TryRollbackAsync(IUnitOfWorkTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private void LogResult(ProcessingResult result)
    {
        switch (result.Outcome)
        {
            case ProcessingOutcome.Created:
                _logger.LogInformation(
                    "Recurring assignment {AssignmentId} produced request {RequestId} and envelope {EnvelopeId}; outcome {Outcome}, reason {Reason}.",
                    result.AssignmentId, result.RequestId, result.EnvelopeId, result.Outcome, result.Reason);
                break;
            case ProcessingOutcome.Deactivated:
                _logger.LogInformation(
                    "Recurring assignment {AssignmentId} processing completed; outcome {Outcome}, reason {Reason}.",
                    result.AssignmentId, result.Outcome, result.Reason);
                break;
            case ProcessingOutcome.Failed:
                _logger.LogWarning(
                    "Recurring assignment {AssignmentId} processing completed; outcome {Outcome}, reason {Reason}, exception type {ExceptionType}.",
                    result.AssignmentId, result.Outcome, result.Reason, result.ExceptionType);
                break;
            case ProcessingOutcome.Aborted:
                LogAborted(result.AssignmentId, (ProcessingReason)result.Reason, result.ExceptionType!);
                break;
            case ProcessingOutcome.Skipped when result.Reason is AutomaticEligibilityReason.NotDue:
                _logger.LogDebug(
                    "Recurring assignment {AssignmentId} processing completed; outcome {Outcome}, reason {Reason}.",
                    result.AssignmentId, result.Outcome, result.Reason);
                break;
            case ProcessingOutcome.Skipped:
                _logger.LogInformation(
                    "Recurring assignment {AssignmentId} processing completed; outcome {Outcome}, reason {Reason}.",
                    result.AssignmentId, result.Outcome, result.Reason);
                break;
        }
    }

    private void LogAborted(
        Id<RecurringReportAssignment> assignmentId,
        ProcessingReason reason,
        string exceptionType)
        => _logger.LogError(
            "Recurring assignment {AssignmentId} processing completed; outcome {Outcome}, reason {Reason}, exception type {ExceptionType}.",
            assignmentId, ProcessingOutcome.Aborted, reason, exceptionType);

    private sealed record ProcessingResult(
        Id<RecurringReportAssignment> AssignmentId,
        ProcessingOutcome Outcome,
        object Reason,
        Id<ReportRequest>? RequestId = null,
        string? EnvelopeId = null,
        string? ExceptionType = null)
    {
        public static ProcessingResult Created(Id<RecurringReportAssignment> id, Id<ReportRequest> requestId, string envelopeId)
            => new(id, ProcessingOutcome.Created, AutomaticEligibilityReason.Eligible, requestId, envelopeId);

        public static ProcessingResult Deactivated(Id<RecurringReportAssignment> id)
            => new(id, ProcessingOutcome.Deactivated, ProcessingReason.TemplateDeleted);

        public static ProcessingResult Skipped(Id<RecurringReportAssignment> id, AutomaticEligibilityReason reason)
            => new(id, ProcessingOutcome.Skipped, reason);

        public static ProcessingResult Failed(Id<RecurringReportAssignment> id, ProcessingReason reason, string exceptionType)
            => new(id, ProcessingOutcome.Failed, reason, ExceptionType: exceptionType);

        public static ProcessingResult Aborted(Id<RecurringReportAssignment> id, ProcessingReason reason, string exceptionType)
            => new(id, ProcessingOutcome.Aborted, reason, ExceptionType: exceptionType);
    }

    private sealed record StagedRequestCreation(Id<ReportRequest> RequestId, string? EnvelopeId);
    private sealed record BusinessRollbackResult(ProcessingResult Result, Exception? Failure);

    private enum TransactionPhase { PreCommit, RolledBack, RollbackFailed, CommitStarted, Committed }
    private enum ProcessingOutcome { Skipped, Created, Deactivated, Failed, Aborted }
    private enum ProcessingReason
    {
        TemplateDeleted,
        EnvelopeMissing,
        PreCommitFailed,
        CancellationRequested,
        BeginTransactionFailed,
        CommitFailed,
        RollbackFailed,
        DisposeFailed
    }
}
