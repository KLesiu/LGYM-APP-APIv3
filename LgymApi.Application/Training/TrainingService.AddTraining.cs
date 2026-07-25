using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.TrainingExecution;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.Training;

public sealed partial class TrainingService
{
    public Task<Result<TrainingSummaryResult, AppError>> AddTrainingAsync(
        Id<LgymApi.Domain.Entities.User> userId,
        AddTrainingInput input,
        CancellationToken cancellationToken = default)
        => _completeTrainingUseCase.AddTrainingAsync(
            userId,
            new CompleteTrainingInput(input.GymId, input.PlanDayId, input.CreatedAt, input.Exercises),
            cancellationToken);
}
