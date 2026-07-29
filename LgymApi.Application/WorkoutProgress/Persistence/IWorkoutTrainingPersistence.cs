using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.WorkoutProgress.Persistence;

public interface IWorkoutTrainingPersistence
{
    Task AddAsync(WorkoutTrainingWriteModel training, CancellationToken cancellationToken = default);
    Task<WorkoutTrainingPersistenceModel?> GetLastByAccountIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutTrainingPersistenceModel>> GetByAccountIdAndDateAsync(Id<AccountReference> accountId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DateTimeOffset>> GetDatesByAccountIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Id<PlanDayReference>, DateTime?>> GetLastTrainingDatesAsync(IReadOnlyCollection<Id<PlanDayReference>> planDayIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutTrainingPersistenceModel>> GetByGymIdsAsync(IReadOnlyCollection<Id<LgymApi.Domain.Entities.Gym>> gymIds, CancellationToken cancellationToken = default);
    Task AddExerciseScoreLinksAsync(IReadOnlyCollection<WorkoutTrainingExerciseScorePersistenceModel> links, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutTrainingExerciseScorePersistenceModel>> GetExerciseScoreLinksAsync(IReadOnlyCollection<Id<LgymApi.Domain.Entities.Training>> trainingIds, CancellationToken cancellationToken = default);
    Task UpdateAccountProfileRankAsync(Id<AccountReference> accountId, string profileRank, CancellationToken cancellationToken = default);
    Task StageTrainingCompletedCommandAsync(Id<AccountReference> accountId, Id<LgymApi.Domain.Entities.Training> trainingId, CancellationToken cancellationToken = default);
}
