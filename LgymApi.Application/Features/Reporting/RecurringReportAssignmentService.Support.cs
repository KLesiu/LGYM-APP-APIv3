using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class RecurringReportAssignmentService
{
    private async Task<Result<RecurringReportAssignmentPersistenceModel, AppError>> GetOwnedAssignmentAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        Id<RecurringReportAssignment> assignmentId,
        CancellationToken cancellationToken)
    {
        var ownershipCheck = await EnsureTrainerOwnsTraineeAsync(currentTrainer, traineeId, cancellationToken);
        if (ownershipCheck.IsFailure)
        {
            return Result<RecurringReportAssignmentPersistenceModel, AppError>.Failure(ownershipCheck.Error);
        }

        if (assignmentId.IsEmpty)
        {
            return Result<RecurringReportAssignmentPersistenceModel, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        var assignment = await _assignmentPersistence.FindForTrainerAsync(
            assignmentId,
            currentTrainer.Id,
            traineeId,
            cancellationToken);
        return assignment == null
            ? Result<RecurringReportAssignmentPersistenceModel, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind))
            : Result<RecurringReportAssignmentPersistenceModel, AppError>.Success(assignment);
    }

    private async Task<Result<ReportTemplatePersistenceModel, AppError>> ValidateTrainerAndCommandAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        UpsertRecurringReportAssignmentCommand command,
        CancellationToken cancellationToken)
    {
        var ownershipCheck = await EnsureTrainerOwnsTraineeAsync(currentTrainer, traineeId, cancellationToken);
        if (ownershipCheck.IsFailure)
        {
            return Result<ReportTemplatePersistenceModel, AppError>.Failure(ownershipCheck.Error);
        }

        if (command.TemplateId.IsEmpty || command.IntervalValue <= 0 || command.StartsAt == default)
        {
            return Result<ReportTemplatePersistenceModel, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        if (command.EndsAt.HasValue && command.EndsAt < command.StartsAt)
        {
            return Result<ReportTemplatePersistenceModel, AppError>.Failure(new InvalidReportingError(Messages.InvalidDateRange));
        }

        var template = await _templatePersistence.FindByIdAsync(command.TemplateId, cancellationToken);
        return template == null || template.TrainerId != currentTrainer.Id || template.IsDeleted
            ? Result<ReportTemplatePersistenceModel, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind))
            : Result<ReportTemplatePersistenceModel, AppError>.Success(template);
    }

    private async Task<Result<Unit, AppError>> EnsureTrainerOwnsTraineeAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken)
    {
        if (!currentTrainer.Roles.Contains(AuthConstants.Roles.Trainer, StringComparer.Ordinal))
        {
            return Result<Unit, AppError>.Failure(new ReportingForbiddenError(Messages.TrainerRoleRequired));
        }

        if (traineeId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.UserIdRequired));
        }

        var access = await _relationshipAccessPersistence.GetAccessAsync(currentTrainer.Id, traineeId, cancellationToken);
        return access.HasActiveRelationship
            ? Result<Unit, AppError>.Success(Unit.Value)
            : Result<Unit, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
    }

    private static string? NormalizeNote(string? note)
        => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static DateTimeOffset AddInterval(DateTimeOffset value, int intervalValue, RecurringReportIntervalUnit intervalUnit)
        => intervalUnit switch
        {
            RecurringReportIntervalUnit.Day => value.AddDays(intervalValue),
            RecurringReportIntervalUnit.Week => value.AddDays(intervalValue * 7d),
            RecurringReportIntervalUnit.Month => value.AddMonths(intervalValue),
            _ => value.AddDays(intervalValue)
        };

    private static DateTimeOffset? RecalculateNextEligibleAt(RecurringReportAssignmentPersistenceModel assignment)
    {
        if (!assignment.IsActive)
        {
            return assignment.NextEligibleAt;
        }

        if (assignment.CurrentReportRequest?.Submission?.TrainerFeedbackReadAt is { } readAt)
        {
            return AddInterval(readAt, assignment.IntervalValue, assignment.IntervalUnit);
        }

        return assignment.CurrentReportRequestId.HasValue ? null : assignment.StartsAt;
    }

    private static AutomaticEligibilityDecision EvaluateAutomaticEligibility(
        RecurringReportAssignmentPersistenceModel assignment,
        DateTimeOffset now)
    {
        if (!assignment.IsActive)
        {
            return AutomaticEligibilityDecision.Blocked(AutomaticEligibilityReason.Inactive);
        }

        if (assignment.StartsAt > now)
        {
            return AutomaticEligibilityDecision.Blocked(AutomaticEligibilityReason.NotStarted);
        }

        if (assignment.EndsAt.HasValue && assignment.EndsAt < now)
        {
            return AutomaticEligibilityDecision.Blocked(AutomaticEligibilityReason.Ended);
        }

        if (!assignment.CurrentReportRequestId.HasValue)
        {
            return (assignment.NextEligibleAt ?? assignment.StartsAt) <= now
                ? AutomaticEligibilityDecision.Eligible()
                : AutomaticEligibilityDecision.Blocked(AutomaticEligibilityReason.NotDue);
        }

        var request = assignment.CurrentReportRequest;
        if (request == null)
        {
            return AutomaticEligibilityDecision.Blocked(AutomaticEligibilityReason.CurrentRequestMissing);
        }

        var statusReason = request.Status switch
        {
            ReportRequestStatus.Submitted => AutomaticEligibilityReason.Eligible,
            ReportRequestStatus.Pending => AutomaticEligibilityReason.CurrentRequestPending,
            ReportRequestStatus.Expired => AutomaticEligibilityReason.CurrentRequestExpired,
            ReportRequestStatus.Cancelled => AutomaticEligibilityReason.CurrentRequestCancelled,
            _ => AutomaticEligibilityReason.CurrentRequestNotSubmitted
        };
        if (statusReason != AutomaticEligibilityReason.Eligible)
        {
            return AutomaticEligibilityDecision.Blocked(statusReason);
        }

        var submission = request.Submission;
        if (submission == null)
        {
            return AutomaticEligibilityDecision.Blocked(AutomaticEligibilityReason.SubmissionMissing);
        }

        if (!submission.TrainerFeedbackAddedAt.HasValue)
        {
            return AutomaticEligibilityDecision.Blocked(AutomaticEligibilityReason.FeedbackNotAdded);
        }

        if (!submission.TrainerFeedbackReadAt.HasValue)
        {
            return AutomaticEligibilityDecision.Blocked(AutomaticEligibilityReason.FeedbackNotRead);
        }

        var eligibleAt = assignment.NextEligibleAt
            ?? AddInterval(submission.TrainerFeedbackReadAt.Value, assignment.IntervalValue, assignment.IntervalUnit);
        return eligibleAt <= now
            ? AutomaticEligibilityDecision.Eligible()
            : AutomaticEligibilityDecision.Blocked(AutomaticEligibilityReason.NotDue);
    }

    private readonly record struct AutomaticEligibilityDecision(
        bool CanCreate,
        AutomaticEligibilityReason Reason)
    {
        public static AutomaticEligibilityDecision Eligible()
            => new(true, AutomaticEligibilityReason.Eligible);

        public static AutomaticEligibilityDecision Blocked(AutomaticEligibilityReason reason)
            => new(false, reason);
    }

    private enum AutomaticEligibilityReason
    {
        Eligible,
        AssignmentMissing,
        NotDue,
        Inactive,
        NotStarted,
        Ended,
        CurrentRequestMissing,
        CurrentRequestPending,
        CurrentRequestExpired,
        CurrentRequestCancelled,
        CurrentRequestNotSubmitted,
        SubmissionMissing,
        FeedbackNotAdded,
        FeedbackNotRead
    }

    private RecurringReportAssignmentResult MapAssignment(RecurringReportAssignmentPersistenceModel assignment)
        => _mapper.Map<RecurringReportAssignmentPersistenceModel, RecurringReportAssignmentResult>(assignment);

    private static RecurringReportAssignmentPersistenceModel ToPersistenceModel(
        NewRecurringReportAssignmentPersistenceModel assignment,
        ReportTemplatePersistenceModel template)
        => new(
            assignment.Id,
            assignment.TrainerId,
            assignment.TraineeId,
            assignment.TemplateId,
            assignment.IntervalValue,
            assignment.IntervalUnit,
            assignment.StartsAt,
            assignment.EndsAt,
            assignment.IsActive,
            assignment.Note,
            assignment.CurrentReportRequestId,
            assignment.LastRequestCreatedAt,
            assignment.NextEligibleAt,
            assignment.CreatedAt,
            false,
            template,
            null);

    private static RecurringReportAssignmentUpdatePersistenceModel ToUpdateModel(
        RecurringReportAssignmentPersistenceModel assignment)
        => new(
            assignment.TemplateId,
            assignment.IntervalValue,
            assignment.IntervalUnit,
            assignment.StartsAt,
            assignment.EndsAt,
            assignment.IsActive,
            assignment.Note,
            assignment.CurrentReportRequestId,
            assignment.LastRequestCreatedAt,
            assignment.NextEligibleAt,
            assignment.IsDeleted);

    private static ReportRequestPersistenceModel ToRequestPersistenceModel(
        NewReportRequestPersistenceModel request,
        ReportTemplatePersistenceModel template)
        => new(
            request.Id,
            request.TrainerId,
            request.TraineeId,
            request.TemplateId,
            request.RecurringReportAssignmentId,
            request.Status,
            request.DueAt,
            request.SubmittedAt,
            request.Note,
            request.CreatedAt,
            false,
            template,
            null);
}
