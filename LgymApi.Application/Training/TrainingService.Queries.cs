using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.TrainingExecution;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.Training;

public sealed partial class TrainingService
{
    public Task<Result<WorkoutTrainingReadModel, AppError>> GetLastTrainingAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
        => _trainingHistoryReadService.GetLastTrainingAsync(userId, cancellationToken);

    public Task<Result<List<TrainingByDateDetails>, AppError>> GetTrainingByDateAsync(
        Id<LgymApi.Identity.Contracts.AccountReference> userId,
        DateTime createdAt,
        CancellationToken cancellationToken = default)
        => _trainingHistoryReadService.GetTrainingByDateAsync(userId, createdAt, cancellationToken);

    public Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
        => _trainingHistoryReadService.GetTrainingDatesAsync(userId, cancellationToken);
}
