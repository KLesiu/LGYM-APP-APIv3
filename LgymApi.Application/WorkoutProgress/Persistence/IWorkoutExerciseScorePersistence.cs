using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.Persistence;

public interface IWorkoutExerciseScorePersistence
{
    Task AddRangeAsync(IReadOnlyCollection<WorkoutExerciseScoreWriteModel> scores, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutExerciseScorePersistenceModel>> GetByIdsAsync(IReadOnlyCollection<Id<LgymApi.Domain.Entities.ExerciseScore>> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutExerciseScorePersistenceModel>> GetByAccountAndExerciseAsync(Id<AccountReference> accountId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutExerciseScorePersistenceModel>> GetByAccountAndExercisesAsync(Id<AccountReference> accountId, IReadOnlyCollection<Id<LgymApi.Domain.Entities.Exercise>> exerciseIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutExerciseScorePersistenceModel>> GetLatestByAccountExerciseSeriesAsync(Id<AccountReference> accountId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, Id<LgymApi.Domain.Entities.Gym>? gymId, CancellationToken cancellationToken = default);
    Task<WorkoutExerciseScorePersistenceModel?> GetBestScoreAsync(Id<AccountReference> accountId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default);
}
