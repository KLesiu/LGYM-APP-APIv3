using System.Text.Json;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class ReportingService : IReportingService
{
    public async Task<Result<ReportSubmissionResult, AppError>> SubmitReportRequestAsync(
        AuthenticatedAccountContext currentTrainee,
        Id<ReportRequest> requestId,
        SubmitReportRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        if (requestId.IsEmpty || command.Answers == null)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        var request = await _requestSubmissionPersistence.FindRequestByIdAsync(requestId, cancellationToken);
        if (request == null || request.TraineeId != currentTrainee.Id)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
        }

        if (request.Status is not (ReportRequestStatus.Pending or ReportRequestStatus.Expired))
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.ReportRequestNotPending));
        }

        if (request.Status == ReportRequestStatus.Pending && IsRequestExpired(request.DueAt, DateTimeOffset.UtcNow))
        {
            await _requestSubmissionPersistence.SetRequestExpiredAsync(request.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            request = request with { Status = ReportRequestStatus.Expired };
        }

        var normalizedAnswers = NormalizeAnswers(command.Answers);
        var validationAnswers = _reportSubmissionAcceptedProgressCommandFactory.FilterInvalidMeasurementAnswers(
            request.Template,
            normalizedAnswers);
        var validationResult = ValidateAnswersAgainstTemplate(request.Template, validationAnswers);
        if (validationResult.IsFailure)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(validationResult.Error);
        }

        var photoValidationResult = await ValidateRequiredPhotosAsync(request, cancellationToken);
        if (photoValidationResult.IsFailure)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(photoValidationResult.Error);
        }

        var submittedAtUtc = DateTimeOffset.UtcNow;
        var submission = new NewReportSubmissionPersistenceModel(
            Id<ReportSubmission>.New(),
            request.Id,
            currentTrainee.Id,
            JsonSerializer.Serialize(normalizedAnswers),
            submittedAtUtc);

        await _requestSubmissionPersistence.AddSubmissionAsync(submission, cancellationToken);
        await _requestSubmissionPersistence.SetRequestSubmittedAsync(request.Id, submittedAtUtc, cancellationToken);

        var acceptedProgressCommand = _reportSubmissionAcceptedProgressCommandFactory.Create(
            request.Template,
            normalizedAnswers,
            submission.Id,
            request.Id,
            currentTrainee.Id,
            submittedAtUtc);
        if (acceptedProgressCommand != null)
        {
            await _commandOutboxWriter.StageAsync(acceptedProgressCommand, cancellationToken);
        }

        var submittedRequest = request with { Status = ReportRequestStatus.Submitted, SubmittedAt = submittedAtUtc };
        var result = await MapAndHydrateSubmissionAsync(
            ToPersistenceModel(submission, submittedRequest),
            cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (IsDuplicateSubmissionException(exception))
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.ReportRequestNotPending));
        }

        await _commandDispatcher.EnqueueAsync(new ReportSubmissionCreatedInAppNotificationCommand
        {
            SubmissionId = submission.Id,
            TrainerId = request.TrainerId,
            TraineeId = currentTrainee.Id,
            TemplateName = request.Template.Name
        });

        return Result<ReportSubmissionResult, AppError>.Success(result);
    }

    public async Task<Result<ReportSubmissionResult, AppError>> UpdateTrainerFeedbackAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        Id<ReportSubmission> submissionId,
        UpdateReportSubmissionFeedbackCommand command,
        CancellationToken cancellationToken = default)
    {
        var ownershipCheck = await EnsureTrainerOwnsTraineeAsync(currentTrainer, traineeId, cancellationToken);
        if (ownershipCheck.IsFailure)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(ownershipCheck.Error);
        }

        if (submissionId.IsEmpty)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        var submission = await _requestSubmissionPersistence.FindSubmissionForTrainerAsync(
            submissionId,
            currentTrainer.Id,
            traineeId,
            cancellationToken);
        if (submission == null)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
        }

        var normalizedFieldComments = NormalizeTrainerFieldComments(command.FieldComments);
        var validationResult = ValidateTrainerFieldComments(submission.ReportRequest.Template, normalizedFieldComments);
        if (validationResult.IsFailure)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(validationResult.Error);
        }

        var overallComment = string.IsNullOrWhiteSpace(command.TrainerOverallComment)
            ? null
            : command.TrainerOverallComment.Trim();
        var fieldCommentsJson = normalizedFieldComments.Count == 0
            ? null
            : JsonSerializer.Serialize(normalizedFieldComments);
        var feedbackChanged = !string.Equals(submission.TrainerOverallComment, overallComment, StringComparison.Ordinal)
            || !string.Equals(submission.TrainerFieldCommentsJson, fieldCommentsJson, StringComparison.Ordinal);
        var hasFeedback = overallComment != null || normalizedFieldComments.Count > 0;
        var feedbackAddedAt = feedbackChanged && hasFeedback ? DateTimeOffset.UtcNow : feedbackChanged ? null : submission.TrainerFeedbackAddedAt;
        var feedbackReadAt = feedbackChanged ? null : submission.TrainerFeedbackReadAt;

        if (feedbackChanged)
        {
            await _requestSubmissionPersistence.UpdateFeedbackAsync(
                submission.Id,
                new ReportSubmissionFeedbackUpdatePersistenceModel(
                    overallComment,
                    fieldCommentsJson,
                    feedbackAddedAt,
                    feedbackReadAt),
                cancellationToken);

            var assignment = await _recurringAssignmentPersistence.FindByCurrentRequestAsync(submission.ReportRequestId, cancellationToken);
            if (assignment != null)
            {
                await _recurringAssignmentPersistence.UpdateAsync(
                    assignment.Id,
                    ToUpdateModel(assignment with { NextEligibleAt = null }),
                    cancellationToken);
            }
        }

        var result = await MapAndHydrateSubmissionAsync(submission with
        {
            TrainerOverallComment = overallComment,
            TrainerFieldCommentsJson = fieldCommentsJson,
            TrainerFeedbackAddedAt = feedbackAddedAt,
            TrainerFeedbackReadAt = feedbackReadAt
        }, cancellationToken);

        if (feedbackChanged && hasFeedback)
        {
            await _commandDispatcher.EnqueueAsync(new ReportFeedbackAddedInAppNotificationCommand
            {
                SubmissionId = submission.Id,
                TraineeId = traineeId,
                TrainerId = currentTrainer.Id,
                TemplateName = submission.ReportRequest.Template.Name,
                TriggeredAt = DateTimeOffset.UtcNow
            });
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ReportSubmissionResult, AppError>.Success(result);
    }

    public async Task<Result<ReportSubmissionResult, AppError>> MarkTrainerFeedbackAsReadAsync(
        AuthenticatedAccountContext currentTrainee,
        Id<ReportSubmission> submissionId,
        CancellationToken cancellationToken = default)
    {
        if (submissionId.IsEmpty)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        var submission = await _requestSubmissionPersistence.FindSubmissionForTraineeAsync(
            submissionId,
            currentTrainee.Id,
            cancellationToken);
        if (submission == null)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
        }

        if (!submission.TrainerFeedbackAddedAt.HasValue)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.DidntFind));
        }

        if (submission.TrainerFeedbackReadAt.HasValue)
        {
            return Result<ReportSubmissionResult, AppError>.Success(
                await MapAndHydrateSubmissionAsync(submission, cancellationToken));
        }

        var readAt = DateTimeOffset.UtcNow;
        await _requestSubmissionPersistence.UpdateFeedbackAsync(
            submission.Id,
            new ReportSubmissionFeedbackUpdatePersistenceModel(
                submission.TrainerOverallComment,
                submission.TrainerFieldCommentsJson,
                submission.TrainerFeedbackAddedAt,
                readAt),
            cancellationToken);

        var assignment = await _recurringAssignmentPersistence.FindByCurrentRequestAsync(submission.ReportRequestId, cancellationToken);
        if (assignment != null)
        {
            await _recurringAssignmentPersistence.UpdateAsync(
                assignment.Id,
                ToUpdateModel(assignment with
                {
                    NextEligibleAt = CalculateNextEligibleAt(readAt, assignment.IntervalValue, assignment.IntervalUnit)
                }),
                cancellationToken);
        }

        var result = await MapAndHydrateSubmissionAsync(
            submission with { TrainerFeedbackReadAt = readAt },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ReportSubmissionResult, AppError>.Success(result);
    }

}
