using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

public interface ITrainingHistoryReadService
{
    Task<Result<Training, AppError>> GetLastTrainingAsync(Id<User> userId, CancellationToken cancellationToken = default);
    Task<Result<List<TrainingByDateDetails>, AppError>> GetTrainingByDateAsync(Id<User> userId, DateTime createdAt, CancellationToken cancellationToken = default);
    Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<User> userId, CancellationToken cancellationToken = default);
}
