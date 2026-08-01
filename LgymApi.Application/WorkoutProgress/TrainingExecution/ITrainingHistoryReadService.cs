using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

public interface ITrainingHistoryReadService
{
    Task<Result<WorkoutTrainingReadModel, AppError>> GetLastTrainingAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<Result<List<TrainingByDateDetails>, AppError>> GetTrainingByDateAsync(Id<AccountReference> accountId, DateTime createdAt, CancellationToken cancellationToken = default);
    Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
}
