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
    public async Task<Result<ReportRequestResult, AppError>> CreateReportRequestAsync(
        AuthenticatedAccountContext currentTrainer,
        Id<AccountReference> traineeId,
        CreateReportRequestCommand command,
        CancellationToken cancellationToken = default)
    {
        var ownershipCheck = await EnsureTrainerOwnsTraineeAsync(currentTrainer, traineeId, cancellationToken);
        if (ownershipCheck.IsFailure)
        {
            return Result<ReportRequestResult, AppError>.Failure(ownershipCheck.Error);
        }

        if (command.TemplateId.IsEmpty)
        {
            return Result<ReportRequestResult, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        var templateResult = await EnsureOwnedTemplateAsync(currentTrainer, command.TemplateId, cancellationToken);
        if (templateResult.IsFailure)
        {
            return Result<ReportRequestResult, AppError>.Failure(templateResult.Error);
        }

        var request = new NewReportRequestPersistenceModel(
            Id<ReportRequest>.New(),
            currentTrainer.Id,
            traineeId,
            templateResult.Value.Id,
            null,
            ReportRequestStatus.Pending,
            NormalizeDueAt(command.DueAt),
            null,
            string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim(),
            DateTimeOffset.UtcNow);

        await _requestSubmissionPersistence.AddRequestAsync(request, cancellationToken);
        await _commandDispatcher.EnqueueAsync(new ReportRequestCreatedInAppNotificationCommand
        {
            RequestId = request.Id,
            TraineeId = traineeId,
            TrainerId = currentTrainer.Id,
            TemplateName = templateResult.Value.Name
        });
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ReportRequestResult, AppError>.Success(
            _mapper.Map<ReportRequestPersistenceModel, ReportRequestResult>(ToPersistenceModel(request, templateResult.Value)));
    }

    public async Task<Result<List<ReportRequestResult>, AppError>> GetPendingRequestsForTraineeAsync(
        AuthenticatedAccountContext currentTrainee,
        CancellationToken cancellationToken = default)
    {
        var requests = await _requestSubmissionPersistence.ListPendingOrExpiredByTraineeAsync(currentTrainee.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var expiredIds = requests
            .Where(request => IsRequestExpired(request.DueAt, now))
            .Select(request => request.Id)
            .ToList();

        foreach (var requestId in expiredIds)
        {
            await _requestSubmissionPersistence.SetRequestExpiredAsync(requestId, cancellationToken);
        }

        if (expiredIds.Count > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            requests = await _requestSubmissionPersistence.ListPendingOrExpiredByTraineeAsync(currentTrainee.Id, cancellationToken);
        }

        return Result<List<ReportRequestResult>, AppError>.Success(
            _mapper.MapList<ReportRequestPersistenceModel, ReportRequestResult>(requests));
    }

    private static ReportRequestPersistenceModel ToPersistenceModel(
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

    private static DateTimeOffset? NormalizeDueAt(DateTimeOffset? dueAt)
    {
        if (!dueAt.HasValue || dueAt.Value.TimeOfDay != TimeSpan.Zero)
        {
            return dueAt;
        }

        var value = dueAt.Value;
        return new DateTimeOffset(value.Year, value.Month, value.Day, 0, 0, 0, value.Offset)
            .AddDays(1)
            .AddTicks(-1);
    }

    private static bool IsRequestExpired(DateTimeOffset? dueAt, DateTimeOffset now)
        => dueAt.HasValue && NormalizeDueAt(dueAt).Value <= now;
}
