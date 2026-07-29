using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Application.WorkoutProgress.Scoring.Elo;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

public interface ICompleteTrainingUseCaseDependencies
{
    IAccountAccessReader AccountAccess { get; }
    IWorkoutGymPersistence GymRepository { get; }
    IPlanDayReferenceReadService PlanDayReferences { get; }
    IWorkoutTrainingPersistence TrainingRepository { get; }
    IWorkoutExercisePersistence ExerciseRepository { get; }
    IWorkoutExerciseScorePersistence ExerciseScoreRepository { get; }
    IWorkoutEloPersistence EloRepository { get; }
    IRankService RankService { get; }
    IUnitOfWork UnitOfWork { get; }
    IReadOnlyCollection<IExerciseEloCalculator> ExerciseEloCalculators { get; }
}

internal sealed class TrainingServiceDependencies(
    IAccountAccessReader accountAccess,
    IWorkoutGymPersistence gymRepository,
    IPlanDayReferenceReadService planDayReferences,
    IWorkoutTrainingPersistence trainingRepository,
    IWorkoutExercisePersistence exerciseRepository,
    IWorkoutExerciseScorePersistence exerciseScoreRepository,
    IWorkoutEloPersistence eloRepository,
    IRankService rankService,
    IUnitOfWork unitOfWork,
    IEnumerable<IExerciseEloCalculator> exerciseEloCalculators) : ICompleteTrainingUseCaseDependencies
{
    public IAccountAccessReader AccountAccess { get; } = accountAccess;
    public IWorkoutGymPersistence GymRepository { get; } = gymRepository;
    public IPlanDayReferenceReadService PlanDayReferences { get; } = planDayReferences;
    public IWorkoutTrainingPersistence TrainingRepository { get; } = trainingRepository;
    public IWorkoutExercisePersistence ExerciseRepository { get; } = exerciseRepository;
    public IWorkoutExerciseScorePersistence ExerciseScoreRepository { get; } = exerciseScoreRepository;
    public IWorkoutEloPersistence EloRepository { get; } = eloRepository;
    public IRankService RankService { get; } = rankService;
    public IUnitOfWork UnitOfWork { get; } = unitOfWork;
    public IReadOnlyCollection<IExerciseEloCalculator> ExerciseEloCalculators { get; } = exerciseEloCalculators.ToArray();
}
