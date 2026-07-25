using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Domain.ValueObjects;
using TrainingEntity = LgymApi.Domain.Entities.Training;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Training;

public interface ITrainingService
{
    Task<Result<TrainingSummaryResult, AppError>> AddTrainingAsync(Id<UserEntity> userId, AddTrainingInput input, CancellationToken cancellationToken = default);
    Task<Result<TrainingEntity, AppError>> GetLastTrainingAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default);
    Task<Result<List<TrainingByDateDetails>, AppError>> GetTrainingByDateAsync(Id<UserEntity> userId, DateTime createdAt, CancellationToken cancellationToken = default);
    Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<UserEntity> userId, CancellationToken cancellationToken = default);
}
