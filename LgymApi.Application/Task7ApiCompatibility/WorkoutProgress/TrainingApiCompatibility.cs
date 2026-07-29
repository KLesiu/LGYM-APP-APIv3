using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Task7ApiCompatibility.WorkoutProgress;

public interface ITrainingApiCompatibilityService
{
    Task<Result<TrainingSummaryResult, AppError>> AddTrainingAsync(Id<AccountReference> accountId, AddTrainingInput input, CancellationToken cancellationToken = default);
    Task<Result<WorkoutTrainingReadModel, AppError>> GetLastTrainingAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<Result<List<TrainingByDateDetails>, AppError>> GetTrainingByDateAsync(Id<AccountReference> accountId, DateTime createdAt, CancellationToken cancellationToken = default);
    Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
}

internal sealed class TrainingApiCompatibilityService : ITrainingApiCompatibilityService
{
    private readonly ITrainingService _trainingService;

    public TrainingApiCompatibilityService(ITrainingService trainingService)
    {
        _trainingService = trainingService;
    }

    public Task<Result<TrainingSummaryResult, AppError>> AddTrainingAsync(Id<AccountReference> accountId, AddTrainingInput input, CancellationToken cancellationToken = default)
        => _trainingService.AddTrainingAsync(accountId, input, cancellationToken);

    public Task<Result<WorkoutTrainingReadModel, AppError>> GetLastTrainingAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => _trainingService.GetLastTrainingAsync(accountId, cancellationToken);

    public Task<Result<List<TrainingByDateDetails>, AppError>> GetTrainingByDateAsync(Id<AccountReference> accountId, DateTime createdAt, CancellationToken cancellationToken = default)
        => _trainingService.GetTrainingByDateAsync(accountId, createdAt, cancellationToken);

    public Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => _trainingService.GetTrainingDatesAsync(accountId, cancellationToken);
}
