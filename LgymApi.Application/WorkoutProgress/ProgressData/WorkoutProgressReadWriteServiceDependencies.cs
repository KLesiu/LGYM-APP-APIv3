using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.Repositories;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Enums;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.WorkoutProgress.ProgressData;

public sealed class WorkoutProgressReadWriteServiceDependencies(
    IWorkoutExercisePersistence exerciseRepository,
    IWorkoutExerciseScorePersistence exerciseScoreRepository,
    IWorkoutMeasurementPersistence measurementRepository,
    IWorkoutMainRecordPersistence mainRecordRepository,
    IWorkoutEloPersistence eloRegistryRepository,
    IAccountAccessReader accountAccess,
    IUnitConverter<HeightUnits> heightUnitConverter,
    IUnitConverter<WeightUnits> weightUnitConverter,
    IUnitOfWork unitOfWork)
{
    public IWorkoutExercisePersistence ExerciseRepository { get; } = exerciseRepository;
    public IWorkoutExerciseScorePersistence ExerciseScoreRepository { get; } = exerciseScoreRepository;
    public IWorkoutMeasurementPersistence MeasurementRepository { get; } = measurementRepository;
    public IWorkoutMainRecordPersistence MainRecordRepository { get; } = mainRecordRepository;
    public IWorkoutEloPersistence EloRegistryRepository { get; } = eloRegistryRepository;
    public IAccountAccessReader AccountAccess { get; } = accountAccess;
    public IUnitConverter<HeightUnits> HeightUnitConverter { get; } = heightUnitConverter;
    public IUnitConverter<WeightUnits> WeightUnitConverter { get; } = weightUnitConverter;
    public IUnitOfWork UnitOfWork { get; } = unitOfWork;
}
