using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

public interface ITrainingHistoryReadServiceDependencies
{
    IAccountAccessReader AccountAccess { get; }
    IWorkoutTrainingPersistence TrainingRepository { get; }
    IWorkoutExerciseScorePersistence ExerciseScoreRepository { get; }
    IPlanDayReferenceReadService PlanDayReferences { get; }
}

internal sealed class TrainingHistoryReadServiceDependencies(
    IAccountAccessReader accountAccess,
    IWorkoutTrainingPersistence trainingRepository,
    IWorkoutExerciseScorePersistence exerciseScoreRepository,
    IPlanDayReferenceReadService planDayReferences) : ITrainingHistoryReadServiceDependencies
{
    public IAccountAccessReader AccountAccess { get; } = accountAccess;
    public IWorkoutTrainingPersistence TrainingRepository { get; } = trainingRepository;
    public IWorkoutExerciseScorePersistence ExerciseScoreRepository { get; } = exerciseScoreRepository;
    public IPlanDayReferenceReadService PlanDayReferences { get; } = planDayReferences;
}
