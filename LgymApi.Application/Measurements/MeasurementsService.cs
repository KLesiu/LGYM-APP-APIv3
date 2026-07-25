using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Measurements.Models;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.WorkoutProgress.Contracts.Measurements;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;
using LgymApi.Resources;

namespace LgymApi.Application.Features.Measurements;

public sealed class MeasurementsService : IMeasurementsService
{
    private readonly IWorkoutProgressReadWriteService _progress;
    private readonly IUserAccessReadService _userAccess;
    private readonly IMeasurementsRelationshipAccessPort _relationshipAccess;

    public MeasurementsService(
        IWorkoutProgressReadWriteService progress,
        IUserAccessReadService userAccess,
        IMeasurementsRelationshipAccessPort relationshipAccess)
    {
        _progress = progress;
        _userAccess = userAccess;
        _relationshipAccess = relationshipAccess;
    }

    public Task<Result<Unit, AppError>> AddMeasurementAsync(UserEntity currentUser, BodyParts bodyPart, MeasurementUnits unit, double value, CancellationToken cancellationToken = default)
        => _progress.AddMeasurementAsync(currentUser?.Id ?? Id<UserEntity>.Empty, bodyPart, unit, value, cancellationToken);

    public Task<Result<Unit, AppError>> AddMeasurementsAsync(UserEntity currentUser, IReadOnlyCollection<MeasurementCreateInput> measurements, CancellationToken cancellationToken = default)
        => _progress.AddMeasurementsAsync(currentUser?.Id ?? Id<UserEntity>.Empty, measurements.Select(item => new MeasurementWriteModel(item.BodyPart, item.Unit, item.Value)).ToList(), cancellationToken);

    public async Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default)
    {
        if (measurementId.IsEmpty)
        {
            return Result<MeasurementReadModel, AppError>.Failure(new InvalidMeasurementError(Messages.InvalidId));
        }

        var owner = await _progress.GetMeasurementOwnerAsync(measurementId, cancellationToken);
        if (owner.IsFailure)
        {
            return Result<MeasurementReadModel, AppError>.Failure(owner.Error);
        }

        var access = await ValidateAccessAsync(currentUser, owner.Value, cancellationToken);
        return access.IsFailure ? Result<MeasurementReadModel, AppError>.Failure(access.Error) : await _progress.GetMeasurementDetailForOwnerAsync(owner.Value, measurementId, cancellationToken);
    }

    public async Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListAsync(UserEntity currentUser, Id<UserEntity> routeUserId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default)
    {
        var access = await ValidateAccessAsync(currentUser, routeUserId, cancellationToken);
        return access.IsFailure ? Result<List<MeasurementReadModel>, AppError>.Failure(access.Error) : await _progress.GetMeasurementsListForOwnerAsync(routeUserId, bodyPart, unit, cancellationToken);
    }

    public async Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryAsync(UserEntity currentUser, Id<UserEntity> routeUserId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default)
    {
        var access = await ValidateAccessAsync(currentUser, routeUserId, cancellationToken);
        return access.IsFailure ? Result<List<MeasurementReadModel>, AppError>.Failure(access.Error) : await _progress.GetMeasurementsHistoryForOwnerAsync(routeUserId, bodyPart, unit, cancellationToken);
    }

    public async Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendAsync(UserEntity currentUser, Id<UserEntity> routeUserId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default)
    {
        var access = await ValidateAccessAsync(currentUser, routeUserId, cancellationToken);
        return access.IsFailure ? Result<MeasurementTrendReadModel, AppError>.Failure(access.Error) : await _progress.GetMeasurementsTrendForOwnerAsync(routeUserId, bodyPart, unit, cancellationToken);
    }

    public async Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsAsync(UserEntity currentUser, Id<UserEntity> routeUserId, CancellationToken cancellationToken = default)
    {
        var access = await ValidateAccessAsync(currentUser, routeUserId, cancellationToken);
        return access.IsFailure ? Result<List<MeasurementTrendReadModel>, AppError>.Failure(access.Error) : await _progress.GetMeasurementsTrendsForOwnerAsync(routeUserId, cancellationToken);
    }

    private async Task<Result<Unit, AppError>> ValidateAccessAsync(UserEntity? currentUser, Id<UserEntity> routeUserId, CancellationToken cancellationToken)
    {
        if (currentUser == null || routeUserId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidMeasurementError(Messages.InvalidId));
        }

        if (currentUser.Id == routeUserId)
        {
            return Result<Unit, AppError>.Success(Unit.Value);
        }

        if (!await _userAccess.IsTrainerAsync(currentUser.Id, cancellationToken) ||
            !await _relationshipAccess.HasActiveRelationshipAsync(currentUser.Id, routeUserId, cancellationToken))
        {
            return Result<Unit, AppError>.Failure(new MeasurementForbiddenError(Messages.Forbidden));
        }

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
