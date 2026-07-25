using System.Text.Json;
using System.Globalization;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class ReportingService : IReportingService
{
    public async Task<Result<ReportSubmissionResult, AppError>> SubmitReportRequestAsync(UserEntity currentTrainee, Id<ReportRequest> requestId, SubmitReportRequestCommand command, CancellationToken cancellationToken = default)
    {
        if (requestId.IsEmpty || command.Answers == null)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        var request = await _reportingRepository.FindRequestByIdAsync(requestId, cancellationToken);
        if (request == null || request.TraineeId != currentTrainee.Id)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
        }

        if (request.Status != ReportRequestStatus.Pending && request.Status != ReportRequestStatus.Expired)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.ReportRequestNotPending));
        }

        if (request.Status == ReportRequestStatus.Pending && IsRequestExpired(request.DueAt, DateTimeOffset.UtcNow))
        {
            request.Status = ReportRequestStatus.Expired;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
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

        var submission = new ReportSubmission
        {
            Id = Id<ReportSubmission>.New(),
            ReportRequestId = request.Id,
            TraineeId = currentTrainee.Id,
            PayloadJson = JsonSerializer.Serialize(normalizedAnswers)
        };

        request.SubmittedAt = submittedAtUtc;
        request.Status = ReportRequestStatus.Submitted;

        await _reportingRepository.AddSubmissionAsync(submission, cancellationToken);
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

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (IsDuplicateSubmissionException(exception))
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.ReportRequestNotPending));
        }

        submission.ReportRequest = request;

        await _commandDispatcher.EnqueueAsync(new ReportSubmissionCreatedInAppNotificationCommand
        {
            SubmissionId = submission.Id,
            TrainerId = request.TrainerId,
            TraineeId = currentTrainee.Id,
            TemplateName = request.Template.Name
        });

        return Result<ReportSubmissionResult, AppError>.Success(MapSubmission(submission));
    }

    public async Task<Result<ReportSubmissionResult, AppError>> UpdateTrainerFeedbackAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, Id<ReportSubmission> submissionId, UpdateReportSubmissionFeedbackCommand command, CancellationToken cancellationToken = default)
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

        var submission = await _reportingRepository.FindSubmissionByIdForTrainerAsync(submissionId, currentTrainer.Id, traineeId, cancellationToken);
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

        var previousOverallComment = submission.TrainerOverallComment;
        var previousFieldCommentsJson = submission.TrainerFieldCommentsJson;

        submission.TrainerOverallComment = string.IsNullOrWhiteSpace(command.TrainerOverallComment)
            ? null
            : command.TrainerOverallComment.Trim();
        submission.TrainerFieldCommentsJson = normalizedFieldComments.Count == 0
            ? null
            : JsonSerializer.Serialize(normalizedFieldComments);

        var feedbackChanged = !string.Equals(previousOverallComment, submission.TrainerOverallComment, StringComparison.Ordinal)
            || !string.Equals(previousFieldCommentsJson, submission.TrainerFieldCommentsJson, StringComparison.Ordinal);
        var hasFeedback = submission.TrainerOverallComment != null || normalizedFieldComments.Count > 0;

        if (feedbackChanged)
        {
            submission.TrainerFeedbackAddedAt = hasFeedback ? DateTimeOffset.UtcNow : null;
            submission.TrainerFeedbackReadAt = null;

            var assignment = await _recurringReportAssignmentRepository.FindByCurrentReportRequestIdAsync(submission.ReportRequestId, cancellationToken);
            if (assignment != null)
            {
                assignment.NextEligibleAt = null;
            }
        }

        if (feedbackChanged && hasFeedback)
        {
            await _commandDispatcher.EnqueueAsync(new ReportFeedbackAddedInAppNotificationCommand
            {
                SubmissionId = submission.Id,
                TraineeId = traineeId,
                TrainerId = currentTrainer.Id,
                TemplateName = submission.ReportRequest.Template.Name,
                // Include a timestamp so distinct feedback saves are not deduplicated
                // by the command envelope hash when they target the same submission.
                TriggeredAt = DateTimeOffset.UtcNow,
            });
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ReportSubmissionResult, AppError>.Success(MapSubmission(submission));
    }

    public async Task<Result<ReportSubmissionResult, AppError>> MarkTrainerFeedbackAsReadAsync(UserEntity currentTrainee, Id<ReportSubmission> submissionId, CancellationToken cancellationToken = default)
    {
        if (submissionId.IsEmpty)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        var submission = await _reportingRepository.FindSubmissionByIdForTraineeAsync(submissionId, currentTrainee.Id, cancellationToken);
        if (submission == null)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new ReportingNotFoundError(Messages.DidntFind));
        }

        if (!submission.TrainerFeedbackAddedAt.HasValue)
        {
            return Result<ReportSubmissionResult, AppError>.Failure(new InvalidReportingError(Messages.DidntFind));
        }

        if (!submission.TrainerFeedbackReadAt.HasValue)
        {
            var readAt = DateTimeOffset.UtcNow;
            submission.TrainerFeedbackReadAt = readAt;

            var assignment = await _recurringReportAssignmentRepository.FindByCurrentReportRequestIdAsync(submission.ReportRequestId, cancellationToken);
            if (assignment != null)
            {
                assignment.NextEligibleAt = CalculateNextEligibleAt(readAt, assignment.IntervalValue, assignment.IntervalUnit);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<ReportSubmissionResult, AppError>.Success(MapSubmission(submission));
    }

    public async Task<Result<List<ReportSubmissionResult>, AppError>> GetTraineeSubmissionsAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, CancellationToken cancellationToken = default)
    {
        var ownershipCheck = await EnsureTrainerOwnsTraineeAsync(currentTrainer, traineeId, cancellationToken);
        if (ownershipCheck.IsFailure)
        {
            return Result<List<ReportSubmissionResult>, AppError>.Failure(ownershipCheck.Error);
        }

        var submissions = await _reportingRepository.GetSubmissionsByTrainerAndTraineeAsync(currentTrainer.Id, traineeId, cancellationToken);
        return Result<List<ReportSubmissionResult>, AppError>.Success(submissions.Select(MapSubmission).ToList());
    }

    public async Task<Result<List<ReportSubmissionResult>, AppError>> GetOwnSubmissionsAsync(UserEntity currentTrainee, CancellationToken cancellationToken = default)
    {
        var submissions = await _reportingRepository.GetSubmissionsByTraineeAsync(currentTrainee.Id, cancellationToken);
        return Result<List<ReportSubmissionResult>, AppError>.Success(submissions.Select(MapSubmission).ToList());
    }

}
