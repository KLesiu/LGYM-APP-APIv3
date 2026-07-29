using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.Dashboard.Models;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Application.WorkoutProgress.TrainingExecution;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.Dashboard;

public sealed class WorkoutProgressDashboardReadService : IWorkoutProgressDashboardReadService
{
    private readonly ITrainingHistoryReadService _trainingHistory;
    private readonly IWorkoutProgressReadWriteService _progress;

    public WorkoutProgressDashboardReadService(
        ITrainingHistoryReadService trainingHistory,
        IWorkoutProgressReadWriteService progress)
    {
        _trainingHistory = trainingHistory;
        _progress = progress;
    }

    public Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<LgymApi.Identity.Contracts.AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _trainingHistory.GetTrainingDatesAsync(traineeId, cancellationToken);

    public async Task<Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>> GetTrainingByDateAsync(
        Id<LgymApi.Identity.Contracts.AccountReference> traineeId,
        DateTime createdAt,
        CancellationToken cancellationToken = default)
    {
        var result = await _trainingHistory.GetTrainingByDateAsync(traineeId, createdAt, cancellationToken);
        return result.IsFailure
            ? Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>.Failure(result.Error)
            : Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>.Success(result.Value.Select(MapTraining).ToList());
    }

    public Task<Result<List<ExerciseScoreChartPoint>, AppError>> GetExerciseScoreChartAsync(
        Id<LgymApi.Identity.Contracts.AccountReference> traineeId,
        string exerciseId,
        CancellationToken cancellationToken = default)
    {
        Id<LgymApi.Domain.Entities.Exercise>.TryParse(exerciseId, out var parsedExerciseId);
        return _progress.GetExerciseScoreChartAsync(traineeId, parsedExerciseId, cancellationToken);
    }

    public Task<Result<List<EloChartPoint>, AppError>> GetEloChartAsync(Id<LgymApi.Identity.Contracts.AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _progress.GetEloChartAsync(traineeId, cancellationToken);

    public Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordHistoryAsync(Id<LgymApi.Identity.Contracts.AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _progress.GetMainRecordHistoryAsync(traineeId, cancellationToken);

    public Task<Result<List<MainRecordBestReadModel>, AppError>> GetBestMainRecordsAsync(Id<LgymApi.Identity.Contracts.AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _progress.GetBestMainRecordsAsync(traineeId, cancellationToken);

    private static WorkoutProgressDashboardTrainingReadModel MapTraining(TrainingByDateDetails training)
    {
        return new WorkoutProgressDashboardTrainingReadModel(
            training.Id.ToString(),
            training.TypePlanDayId.ToString(),
            training.CreatedAt,
            training.PlanDay == null ? null : new WorkoutProgressDashboardPlanDayReadModel(training.PlanDay.PlanDayId.ToString(), training.PlanDay.Name),
            training.Gym,
            training.Exercises.Select(MapExercise).ToList());
    }

    private static WorkoutProgressDashboardExerciseReadModel MapExercise(EnrichedExercise exercise)
    {
        return new WorkoutProgressDashboardExerciseReadModel(
            exercise.ExerciseScoreId.ToString(),
            new WorkoutProgressDashboardExerciseDetailsReadModel(
                exercise.ExerciseDetails.Id.ToString(),
                exercise.ExerciseDetails.Name,
                exercise.ExerciseDetails.UserId?.ToString(),
                exercise.ExerciseDetails.BodyPart,
                exercise.ExerciseDetails.EloFormula,
                exercise.ExerciseDetails.Description,
                exercise.ExerciseDetails.Image),
            exercise.ScoresDetails.Select(score => new WorkoutProgressDashboardExerciseScoreReadModel(
                score.Id.ToString(),
                score.ExerciseId.ToString(),
                score.Weight,
                score.Unit,
                score.Reps,
                score.Series)).ToList());
    }
}
