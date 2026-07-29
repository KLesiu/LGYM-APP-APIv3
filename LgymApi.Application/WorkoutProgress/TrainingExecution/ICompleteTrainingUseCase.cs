using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

public interface ICompleteTrainingUseCase
{
    Task<Result<TrainingSummaryResult, AppError>> AddTrainingAsync(
        Id<AccountReference> userId,
        CompleteTrainingInput input,
        CancellationToken cancellationToken = default);
}
