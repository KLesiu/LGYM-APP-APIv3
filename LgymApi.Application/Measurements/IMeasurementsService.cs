using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Measurements.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Measurements;

public interface IMeasurementsService
{
    Task<Result<Unit, AppError>> AddMeasurementAsync(UserEntity currentUser, BodyParts bodyPart, MeasurementUnits unit, double value, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddMeasurementsAsync(UserEntity currentUser, IReadOnlyCollection<MeasurementCreateInput> measurements, CancellationToken cancellationToken = default);
    Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListAsync(UserEntity currentUser, Id<UserEntity> routeUserId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryAsync(UserEntity currentUser, Id<UserEntity> routeUserId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendAsync(UserEntity currentUser, Id<UserEntity> routeUserId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsAsync(UserEntity currentUser, Id<UserEntity> routeUserId, CancellationToken cancellationToken = default);
}
