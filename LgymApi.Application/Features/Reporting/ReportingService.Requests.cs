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
    public async Task<Result<ReportRequestResult, AppError>> CreateReportRequestAsync(UserEntity currentTrainer, Id<UserEntity> traineeId, CreateReportRequestCommand command, CancellationToken cancellationToken = default)
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

        var request = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = currentTrainer.Id,
            TraineeId = traineeId,
            TemplateId = templateResult.Value.Id,
            Status = ReportRequestStatus.Pending,
            DueAt = NormalizeDueAt(command.DueAt),
            Note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim()
        };

        await _reportingRepository.AddRequestAsync(request, cancellationToken);
        // Queue notification before commit so the worker dispatch stays bound to the same unit of work.
        await _commandDispatcher.EnqueueAsync(new ReportRequestCreatedInAppNotificationCommand
        {
            RequestId = request.Id,
            TraineeId = traineeId,
            TrainerId = currentTrainer.Id,
            TemplateName = templateResult.Value.Name
        });
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        request.Template = templateResult.Value;
        return Result<ReportRequestResult, AppError>.Success(MapRequest(request));
    }

    public async Task<Result<List<ReportRequestResult>, AppError>> GetPendingRequestsForTraineeAsync(UserEntity currentTrainee, CancellationToken cancellationToken = default)
    {
        var requests = await _reportingRepository.GetPendingOrExpiredRequestsByTraineeIdAsync(currentTrainee.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var hasUpdates = false;

        foreach (var request in requests)
        {
            if (IsRequestExpired(request.DueAt, now))
            {
                request.Status = ReportRequestStatus.Expired;
                hasUpdates = true;
            }
        }

        if (hasUpdates)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            requests = await _reportingRepository.GetPendingOrExpiredRequestsByTraineeIdAsync(currentTrainee.Id, cancellationToken);
        }

        return Result<List<ReportRequestResult>, AppError>.Success(requests.Select(MapRequest).ToList());
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
            Template = MapTemplate(request.Template)
        };
    }

    private static DateTimeOffset? NormalizeDueAt(DateTimeOffset? dueAt)
    {
        if (!dueAt.HasValue)
        {
            return null;
        }

        var value = dueAt.Value;
        if (value.TimeOfDay != TimeSpan.Zero)
        {
            return value;
        }

        return new DateTimeOffset(value.Year, value.Month, value.Day, 0, 0, 0, value.Offset)
            .AddDays(1)
            .AddTicks(-1);
    }

    private static bool IsRequestExpired(DateTimeOffset? dueAt, DateTimeOffset now)
        => dueAt.HasValue && NormalizeDueAt(dueAt).Value <= now;
}
