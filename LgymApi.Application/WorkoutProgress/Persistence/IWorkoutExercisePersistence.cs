using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.Persistence;

public interface IWorkoutExercisePersistence
{
    Task<WorkoutExercisePersistenceModel?> FindByIdAsync(Id<LgymApi.Domain.Entities.Exercise> id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutExercisePersistenceModel>> GetAllForAccountAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutExercisePersistenceModel>> GetAllGlobalAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutExercisePersistenceModel>> GetAccountExercisesAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutExercisePersistenceModel>> GetByBodyPartAsync(Id<AccountReference> accountId, BodyParts bodyPart, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkoutExercisePersistenceModel>> GetByIdsAsync(IReadOnlyCollection<Id<LgymApi.Domain.Entities.Exercise>> ids, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Id<LgymApi.Domain.Entities.Exercise>, string>> GetTranslationsAsync(IReadOnlyCollection<Id<LgymApi.Domain.Entities.Exercise>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task UpsertTranslationAsync(Id<LgymApi.Domain.Entities.Exercise> exerciseId, string culture, string name, CancellationToken cancellationToken = default);
    Task AddAsync(WorkoutExerciseWriteModel exercise, CancellationToken cancellationToken = default);
    Task UpdateAsync(WorkoutExerciseWriteModel exercise, CancellationToken cancellationToken = default);
}
