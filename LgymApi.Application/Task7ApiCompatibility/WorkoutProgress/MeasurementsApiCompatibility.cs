using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Measurements;
using LgymApi.Application.Features.Measurements.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Task7ApiCompatibility.WorkoutProgress;

public interface IMeasurementsApiCompatibilityService
{
    Task<Result<Unit, AppError>> AddMeasurementAsync(AuthenticatedAccountContext? currentAccount, BodyParts bodyPart, MeasurementUnits unit, double value, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AddMeasurementsAsync(AuthenticatedAccountContext? currentAccount, IReadOnlyCollection<MeasurementCreateInput> measurements, CancellationToken cancellationToken = default);
    Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailAsync(AuthenticatedAccountContext? currentAccount, Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default);
    Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default);
    Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, CancellationToken cancellationToken = default);
}

internal sealed class MeasurementsApiCompatibilityService : IMeasurementsApiCompatibilityService
{
    private readonly IMeasurementsService _measurementsService;

    public MeasurementsApiCompatibilityService(IMeasurementsService measurementsService)
    {
        _measurementsService = measurementsService;
    }

    public async Task<Result<Unit, AppError>> AddMeasurementAsync(AuthenticatedAccountContext? currentAccount, BodyParts bodyPart, MeasurementUnits unit, double value, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _measurementsService.AddMeasurementAsync(null, bodyPart, unit, value, cancellationToken);
        }

        return await _measurementsService.AddMeasurementAsync(currentAccount, bodyPart, unit, value, cancellationToken);
    }

    public async Task<Result<Unit, AppError>> AddMeasurementsAsync(AuthenticatedAccountContext? currentAccount, IReadOnlyCollection<MeasurementCreateInput> measurements, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _measurementsService.AddMeasurementsAsync(null, measurements, cancellationToken);
        }

        return await _measurementsService.AddMeasurementsAsync(currentAccount, measurements, cancellationToken);
    }

    public async Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailAsync(AuthenticatedAccountContext? currentAccount, Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _measurementsService.GetMeasurementDetailAsync(null, measurementId, cancellationToken);
        }

        return await _measurementsService.GetMeasurementDetailAsync(currentAccount, measurementId, cancellationToken);
    }

    public async Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _measurementsService.GetMeasurementsListAsync(null, routeAccountId, bodyPart, unit, cancellationToken);
        }

        return await _measurementsService.GetMeasurementsListAsync(currentAccount, routeAccountId, bodyPart, unit, cancellationToken);
    }

    public async Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _measurementsService.GetMeasurementsHistoryAsync(null, routeAccountId, bodyPart, unit, cancellationToken);
        }

        return await _measurementsService.GetMeasurementsHistoryAsync(currentAccount, routeAccountId, bodyPart, unit, cancellationToken);
    }

    public async Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _measurementsService.GetMeasurementsTrendAsync(null, routeAccountId, bodyPart, unit, cancellationToken);
        }

        return await _measurementsService.GetMeasurementsTrendAsync(currentAccount, routeAccountId, bodyPart, unit, cancellationToken);
    }

    public async Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, CancellationToken cancellationToken = default)
    {
        if (currentAccount is null)
        {
            return await _measurementsService.GetMeasurementsTrendsAsync(null, routeAccountId, cancellationToken);
        }

        return await _measurementsService.GetMeasurementsTrendsAsync(currentAccount, routeAccountId, cancellationToken);
    }

}
