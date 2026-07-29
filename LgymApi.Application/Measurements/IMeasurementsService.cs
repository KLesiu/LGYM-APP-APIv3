using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Measurements.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Features.Measurements;

public interface IMeasurementsService
{
    Task<Result<Unit, AppError>> AddMeasurementAsync(AuthenticatedAccountContext? currentAccount, BodyParts bodyPart, MeasurementUnits unit, double value, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddMeasurementsAsync(AuthenticatedAccountContext? currentAccount, IReadOnlyCollection<MeasurementCreateInput> measurements, CancellationToken cancellationToken = default);
    Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailAsync(AuthenticatedAccountContext? currentAccount, Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, CancellationToken cancellationToken = default);
}
