using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Domain.Enums;

namespace LgymApi.Application.Features.MainRecords;

public interface IMainRecordsServiceDependencies
{
    IUserRepository UserRepository { get; }
    IExerciseRepository ExerciseRepository { get; }
    IMainRecordRepository MainRecordRepository { get; }
    IExerciseScoreRepository ExerciseScoreRepository { get; }
    IUnitConverter<WeightUnits> WeightUnitConverter { get; }
    IUnitOfWork UnitOfWork { get; }
}

internal sealed class MainRecordsServiceDependencies : IMainRecordsServiceDependencies
{
    public MainRecordsServiceDependencies(
        IUserRepository userRepository,
        IExerciseRepository exerciseRepository,
        IMainRecordRepository mainRecordRepository,
        IExerciseScoreRepository exerciseScoreRepository,
        IUnitConverter<WeightUnits> weightUnitConverter,
        IUnitOfWork unitOfWork)
    {
        UserRepository = userRepository;
        ExerciseRepository = exerciseRepository;
        MainRecordRepository = mainRecordRepository;
        ExerciseScoreRepository = exerciseScoreRepository;
        WeightUnitConverter = weightUnitConverter;
        UnitOfWork = unitOfWork;
    }

    public IUserRepository UserRepository { get; }
    public IExerciseRepository ExerciseRepository { get; }
    public IMainRecordRepository MainRecordRepository { get; }
    public IExerciseScoreRepository ExerciseScoreRepository { get; }
    public IUnitConverter<WeightUnits> WeightUnitConverter { get; }
    public IUnitOfWork UnitOfWork { get; }
}
