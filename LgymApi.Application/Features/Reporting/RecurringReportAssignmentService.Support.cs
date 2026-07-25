using System.Text.Json;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class RecurringReportAssignmentService
{
    private async Task<Result<RecurringReportAssignment, AppError>> GetOwnedAssignmentAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<RecurringReportAssignment> assignmentId, CancellationToken cancellationToken)
    {
        var ownershipCheck = await EnsureTrainerOwnsTraineeAsync(currentTrainer, traineeId, cancellationToken);
        if (ownershipCheck.IsFailure)
        {
            return Result<RecurringReportAssignment, AppError>.Failure(ownershipCheck.Error);
        }

        if (assignmentId.IsEmpty)
        {
            return Result<RecurringReportAssignment, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        var assignment = await _assignmentRepository.FindByIdForTrainerAsync(assignmentId, currentTrainer.Id, traineeId, cancellationToken);
        if (assignment == null)
        {
            return Result<RecurringReportAssignment, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
        }

        return Result<RecurringReportAssignment, AppError>.Success(assignment);
    }

    private async Task<Result<ReportTemplate, AppError>> ValidateTrainerAndCommandAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, UpsertRecurringReportAssignmentCommand command, CancellationToken cancellationToken)
    {
        var ownershipCheck = await EnsureTrainerOwnsTraineeAsync(currentTrainer, traineeId, cancellationToken);
        if (ownershipCheck.IsFailure)
        {
            return Result<ReportTemplate, AppError>.Failure(ownershipCheck.Error);
        }

        if (command.TemplateId.IsEmpty || command.IntervalValue <= 0 || command.StartsAt == default)
        {
            return Result<ReportTemplate, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        if (command.EndsAt.HasValue && command.EndsAt < command.StartsAt)
        {
            return Result<ReportTemplate, AppError>.Failure(new InvalidReportingError(Messages.InvalidDateRange));
        }

        var template = await _reportingRepository.FindTemplateByIdAsync(command.TemplateId, cancellationToken);
        if (template == null || template.TrainerId != currentTrainer.Id || template.IsDeleted)
        {
            return Result<ReportTemplate, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
        }

        return Result<ReportTemplate, AppError>.Success(template);
    }

    private async Task<Result<Unit, AppError>> EnsureTrainerOwnsTraineeAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, CancellationToken cancellationToken)
    {
        var access = await _coachingRelationshipAccessService.GetAccessDecisionAsync(
            currentTrainer.Id,
            traineeId,
            cancellationToken);
        if (!access.IsTrainer)
        {
            return Result<Unit, AppError>.Failure(new ReportingForbiddenError(Messages.TrainerRoleRequired));
        }

        if (traineeId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.UserIdRequired));
        }

        if (!access.HasActiveRelationship)
        {
            return Result<Unit, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
        }

        return Result<Unit, AppError>.Success(Unit.Value);
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

    private static DateTimeOffset? RecalculateNextEligibleAt(RecurringReportAssignment assignment)
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

    private static bool CanCreateNextRequest(RecurringReportAssignment assignment, DateTimeOffset now)
    {
        if (!assignment.IsActive || assignment.StartsAt > now)
        {
            return false;
        }

        if (assignment.EndsAt.HasValue && assignment.EndsAt < now)
        {
            return false;
        }

        if (!assignment.CurrentReportRequestId.HasValue)
        {
            return (assignment.NextEligibleAt ?? assignment.StartsAt) <= now;
        }

        var currentRequest = assignment.CurrentReportRequest;
        var submission = currentRequest?.Submission;
        if (currentRequest == null || submission == null)
        {
            return false;
        }

        if (currentRequest.Status is ReportRequestStatus.Pending or ReportRequestStatus.Expired)
        {
            return false;
        }

        if (!submission.TrainerFeedbackAddedAt.HasValue || !submission.TrainerFeedbackReadAt.HasValue)
        {
            return false;
        }

        var eligibleAt = assignment.NextEligibleAt ?? AddInterval(submission.TrainerFeedbackReadAt.Value, assignment.IntervalValue, assignment.IntervalUnit);
        return eligibleAt <= now;
    }

    private static RecurringReportAssignmentResult MapAssignment(RecurringReportAssignment assignment)
    {
        return new RecurringReportAssignmentResult
        {
            Id = assignment.Id,
            TrainerId = assignment.TrainerId,
            TraineeId = assignment.TraineeId,
            TemplateId = assignment.TemplateId,
            IntervalValue = assignment.IntervalValue,
            IntervalUnit = assignment.IntervalUnit,
            StartsAt = assignment.StartsAt,
            EndsAt = assignment.EndsAt,
            IsActive = assignment.IsActive,
            Note = assignment.Note,
            CurrentReportRequestId = assignment.CurrentReportRequestId,
            LastRequestCreatedAt = assignment.LastRequestCreatedAt,
            NextEligibleAt = assignment.NextEligibleAt,
            CreatedAt = assignment.CreatedAt,
            Template = MapTemplate(assignment.Template),
            CurrentReportRequest = assignment.CurrentReportRequest == null ? null : MapRequest(assignment.CurrentReportRequest)
        };
    }

    private static ReportTemplateResult MapTemplate(ReportTemplate template)
    {
        return new ReportTemplateResult
        {
            Id = template.Id,
            TrainerId = template.TrainerId,
            Name = template.Name,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            Fields = template.Fields
                .OrderBy(x => x.Order)
                .ThenBy(x => x.CreatedAt)
                .Select(x => new ReportTemplateFieldResult
                {
                    Key = x.Key,
                    Label = x.Label,
                    Type = x.Type,
                    IsRequired = x.IsRequired,
                    Order = x.Order,
                    ModuleConfig = x.ModuleConfig == null ? null : JsonSerializer.Deserialize<JsonElement>(x.ModuleConfig)
                })
                .ToList()
        };
    }

    private static ReportRequestResult MapRequest(ReportRequest request)
    {
        return new ReportRequestResult
        {
            Id = request.Id,
            TrainerId = request.TrainerId,
            TraineeId = request.TraineeId,
            TemplateId = request.TemplateId,
            Status = request.Status,
            DueAt = request.DueAt,
            Note = request.Note,
            CreatedAt = request.CreatedAt,
            SubmittedAt = request.SubmittedAt,
            Template = request.Template == null ? new ReportTemplateResult() : MapTemplate(request.Template)
        };
    }
}
