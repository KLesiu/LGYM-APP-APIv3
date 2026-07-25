using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Domain.Enums;

namespace LgymApi.Application.WorkoutProgress.ProgressData;

public sealed class WorkoutProgressReadWriteServiceDependencies(
    IExerciseRepository exerciseRepository,
    IExerciseScoreRepository exerciseScoreRepository,
    IMeasurementRepository measurementRepository,
    IMainRecordRepository mainRecordRepository,
    IEloRegistryRepository eloRegistryRepository,
    IUserAccessReadService userAccess,
    IUnitConverter<HeightUnits> heightUnitConverter,
    IUnitConverter<WeightUnits> weightUnitConverter,
    IUnitOfWork unitOfWork)
{
    public IExerciseRepository ExerciseRepository { get; } = exerciseRepository;
    public IExerciseScoreRepository ExerciseScoreRepository { get; } = exerciseScoreRepository;
    public IMeasurementRepository MeasurementRepository { get; } = measurementRepository;
    public IMainRecordRepository MainRecordRepository { get; } = mainRecordRepository;
    public IEloRegistryRepository EloRegistryRepository { get; } = eloRegistryRepository;
    public IUserAccessReadService UserAccess { get; } = userAccess;
    public IUnitConverter<HeightUnits> HeightUnitConverter { get; } = heightUnitConverter;
    public IUnitConverter<WeightUnits> WeightUnitConverter { get; } = weightUnitConverter;
    public IUnitOfWork UnitOfWork { get; } = unitOfWork;
}
