using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.Training;

public interface ITrainingService
{
    Task<Result<TrainingSummaryResult, AppError>> AddTrainingAsync(Id<LgymApi.Identity.Contracts.AccountReference> accountId, AddTrainingInput input, CancellationToken cancellationToken = default);
    Task<Result<WorkoutTrainingReadModel, AppError>> GetLastTrainingAsync(Id<LgymApi.Identity.Contracts.AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<Result<List<TrainingByDateDetails>, AppError>> GetTrainingByDateAsync(Id<LgymApi.Identity.Contracts.AccountReference> accountId, DateTime createdAt, CancellationToken cancellationToken = default);
    Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<LgymApi.Identity.Contracts.AccountReference> accountId, CancellationToken cancellationToken = default);
}
