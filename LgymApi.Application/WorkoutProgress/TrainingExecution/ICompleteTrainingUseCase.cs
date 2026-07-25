using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

public interface ICompleteTrainingUseCase
{
    Task<Result<TrainingSummaryResult, AppError>> AddTrainingAsync(
        Id<User> userId,
        CompleteTrainingInput input,
        CancellationToken cancellationToken = default);
}
