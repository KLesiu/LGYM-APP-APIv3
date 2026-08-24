using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.ProgressData;

public interface IWorkoutProgressReadWriteService
{
    Task<Result<List<ExerciseScoreChartPoint>, AppError>> GetExerciseScoreChartAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddMeasurementAsync(Id<LgymApi.Identity.Contracts.AccountReference> currentUserId, BodyParts bodyPart, MeasurementUnits unit, double value, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddMeasurementsAsync(Id<LgymApi.Identity.Contracts.AccountReference> currentUserId, IReadOnlyCollection<MeasurementWriteModel> measurements, CancellationToken cancellationToken = default);
    Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailAsync(Id<LgymApi.Identity.Contracts.AccountReference> currentUserId, Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListAsync(Id<LgymApi.Identity.Contracts.AccountReference> currentUserId, Id<LgymApi.Identity.Contracts.AccountReference> routeUserId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryAsync(Id<LgymApi.Identity.Contracts.AccountReference> currentUserId, Id<LgymApi.Identity.Contracts.AccountReference> routeUserId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendAsync(Id<LgymApi.Identity.Contracts.AccountReference> currentUserId, Id<LgymApi.Identity.Contracts.AccountReference> routeUserId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsAsync(Id<LgymApi.Identity.Contracts.AccountReference> currentUserId, Id<LgymApi.Identity.Contracts.AccountReference> routeUserId, CancellationToken cancellationToken = default);
    Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailForOwnerAsync(Id<LgymApi.Identity.Contracts.AccountReference> ownerId, Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default);
    Task<Result<Id<LgymApi.Identity.Contracts.AccountReference>, AppError>> GetMeasurementOwnerAsync(Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListForOwnerAsync(Id<LgymApi.Identity.Contracts.AccountReference> ownerId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryForOwnerAsync(Id<LgymApi.Identity.Contracts.AccountReference> ownerId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendForOwnerAsync(Id<LgymApi.Identity.Contracts.AccountReference> ownerId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsForOwnerAsync(Id<LgymApi.Identity.Contracts.AccountReference> ownerId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddMainRecordAsync(MainRecordCreateWriteModel input, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordHistoryAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordBestReadModel>, AppError>> GetBestMainRecordsAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteMainRecordAsync(Id<LgymApi.Identity.Contracts.AccountReference> currentUserId, Id<LgymApi.Domain.Entities.MainRecord> recordId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateMainRecordAsync(MainRecordUpdateWriteModel input, CancellationToken cancellationToken = default);
    Task<Result<PossibleRecordReadModel, AppError>> GetRecordOrPossibleRecordAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Id<LgymApi.Domain.Entities.Exercise>, string>> GetExerciseDisplayNamesAsync(IEnumerable<Id<LgymApi.Domain.Entities.Exercise>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<List<EloChartPoint>, AppError>> GetEloChartAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default);
    Task<Result<int, AppError>> GetLatestEloAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default);
    Task<int> GetLatestEloOrDefaultAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default);
    Task InitializeEloAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default);
}
