using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class ReportingService
{
    public async Task<Result<List<ReportSubmissionResult>, AppError>> GetTraineeSubmissionsAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        CancellationToken cancellationToken = default)
    {
        var ownershipCheck = await EnsureTrainerOwnsTraineeAsync(currentTrainer, traineeId, cancellationToken);
        if (ownershipCheck.IsFailure)
        {
            return Result<List<ReportSubmissionResult>, AppError>.Failure(ownershipCheck.Error);
        }

        var submissions = await _requestSubmissionPersistence.ListSubmissionsByTrainerAndTraineeAsync(
            currentTrainer.Id,
            traineeId,
            cancellationToken);
        return Result<List<ReportSubmissionResult>, AppError>.Success(
            _mapper.MapList<ReportSubmissionPersistenceModel, ReportSubmissionResult>(submissions));
    }

    public async Task<Result<List<ReportSubmissionResult>, AppError>> GetOwnSubmissionsAsync(
        AuthenticatedAccountContext currentTrainee,
        CancellationToken cancellationToken = default)
    {
        var submissions = await _requestSubmissionPersistence.ListSubmissionsByTraineeAsync(currentTrainee.Id, cancellationToken);
        return Result<List<ReportSubmissionResult>, AppError>.Success(
            _mapper.MapList<ReportSubmissionPersistenceModel, ReportSubmissionResult>(submissions));
    }

    private ReportSubmissionResult MapSubmission(ReportSubmissionPersistenceModel submission)
        => _mapper.Map<ReportSubmissionPersistenceModel, ReportSubmissionResult>(submission);

    private static ReportSubmissionPersistenceModel ToPersistenceModel(
        NewReportSubmissionPersistenceModel submission,
        ReportRequestPersistenceModel request)
        => new(
            submission.Id,
            submission.ReportRequestId,
            submission.TraineeId,
            submission.PayloadJson,
            null,
            null,
            null,
            null,
            submission.CreatedAt,
            request);

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
}
