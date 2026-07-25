using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.ProgressData;

public interface IWorkoutProgressReadWriteService
{
    Task<Result<List<ExerciseScoreChartPoint>, AppError>> GetExerciseScoreChartAsync(Id<LgymApi.Domain.Entities.User> userId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddMeasurementAsync(Id<LgymApi.Domain.Entities.User> currentUserId, BodyParts bodyPart, MeasurementUnits unit, double value, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddMeasurementsAsync(Id<LgymApi.Domain.Entities.User> currentUserId, IReadOnlyCollection<MeasurementWriteModel> measurements, CancellationToken cancellationToken = default);
    Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailAsync(Id<LgymApi.Domain.Entities.User> currentUserId, Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListAsync(Id<LgymApi.Domain.Entities.User> currentUserId, Id<LgymApi.Domain.Entities.User> routeUserId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryAsync(Id<LgymApi.Domain.Entities.User> currentUserId, Id<LgymApi.Domain.Entities.User> routeUserId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendAsync(Id<LgymApi.Domain.Entities.User> currentUserId, Id<LgymApi.Domain.Entities.User> routeUserId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsAsync(Id<LgymApi.Domain.Entities.User> currentUserId, Id<LgymApi.Domain.Entities.User> routeUserId, CancellationToken cancellationToken = default);
    Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailForOwnerAsync(Id<LgymApi.Domain.Entities.User> ownerId, Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default);
    Task<Result<Id<LgymApi.Domain.Entities.User>, AppError>> GetMeasurementOwnerAsync(Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListForOwnerAsync(Id<LgymApi.Domain.Entities.User> ownerId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryForOwnerAsync(Id<LgymApi.Domain.Entities.User> ownerId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendForOwnerAsync(Id<LgymApi.Domain.Entities.User> ownerId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsForOwnerAsync(Id<LgymApi.Domain.Entities.User> ownerId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddMainRecordAsync(MainRecordCreateWriteModel input, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordHistoryAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordBestReadModel>, AppError>> GetBestMainRecordsAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteMainRecordAsync(Id<LgymApi.Domain.Entities.User> currentUserId, Id<LgymApi.Domain.Entities.MainRecord> recordId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateMainRecordAsync(MainRecordUpdateWriteModel input, CancellationToken cancellationToken = default);
    Task<Result<PossibleRecordReadModel, AppError>> GetRecordOrPossibleRecordAsync(Id<LgymApi.Domain.Entities.User> userId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default);
    Task<Result<List<EloChartPoint>, AppError>> GetEloChartAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
    Task<Result<int, AppError>> GetLatestEloAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
    Task<int> GetLatestEloOrDefaultAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
    Task InitializeEloAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
}
