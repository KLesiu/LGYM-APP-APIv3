using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.Dashboard.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.Dashboard;

public interface IWorkoutProgressDashboardReadService
{
    Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(Id<LgymApi.Domain.Entities.User> traineeId, CancellationToken cancellationToken = default);
    Task<Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>> GetTrainingByDateAsync(Id<LgymApi.Domain.Entities.User> traineeId, DateTime createdAt, CancellationToken cancellationToken = default);
    Task<Result<List<ExerciseScoreChartPoint>, AppError>> GetExerciseScoreChartAsync(Id<LgymApi.Domain.Entities.User> traineeId, string exerciseId, CancellationToken cancellationToken = default);
    Task<Result<List<EloChartPoint>, AppError>> GetEloChartAsync(Id<LgymApi.Domain.Entities.User> traineeId, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordHistoryAsync(Id<LgymApi.Domain.Entities.User> traineeId, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordBestReadModel>, AppError>> GetBestMainRecordsAsync(Id<LgymApi.Domain.Entities.User> traineeId, CancellationToken cancellationToken = default);
}
