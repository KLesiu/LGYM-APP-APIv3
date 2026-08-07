using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.WorkoutProgress;

public sealed class WorkoutExercisePersistenceRepository(AppDbContext dbContext) : IWorkoutExercisePersistence
{
    public Task<WorkoutExercisePersistenceModel?> FindByIdAsync(Id<Exercise> id, CancellationToken cancellationToken = default)
        => FindUnrestrictedByIdAsync(id, cancellationToken);

    public Task<WorkoutExercisePersistenceModel?> FindUnrestrictedByIdAsync(Id<Exercise> id, CancellationToken cancellationToken = default)
        => FindAsync(dbContext.Exercises.Where(exercise => exercise.Id == id), cancellationToken);

    public Task<WorkoutExercisePersistenceModel?> FindVisibleToAccountAsync(Id<Exercise> id, Id<AccountReference> accountId, CancellationToken cancellationToken = default)
    {
        var persistedAccountId = WorkoutPersistenceAccountIds.ToPersisted(accountId);
        return FindAsync(
            dbContext.Exercises.Where(exercise => exercise.Id == id && (exercise.UserId == null || exercise.UserId == persistedAccountId)),
            cancellationToken);
    }

    public Task<WorkoutExercisePersistenceModel?> FindOwnedByAccountAsync(Id<Exercise> id, Id<AccountReference> accountId, CancellationToken cancellationToken = default)
    {
        var persistedAccountId = WorkoutPersistenceAccountIds.ToPersisted(accountId);
        return FindAsync(
            dbContext.Exercises.Where(exercise => exercise.Id == id && exercise.UserId == persistedAccountId),
            cancellationToken);
    }

    public Task<IReadOnlyList<WorkoutExercisePersistenceModel>> GetAllForAccountAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => ReadAsync(dbContext.Exercises.Where(exercise => exercise.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId) || exercise.UserId == null), cancellationToken);

    public Task<IReadOnlyList<WorkoutExercisePersistenceModel>> GetAllGlobalAsync(CancellationToken cancellationToken = default)
        => ReadAsync(dbContext.Exercises.Where(exercise => exercise.UserId == null && !exercise.IsDeleted), cancellationToken);

    public Task<IReadOnlyList<WorkoutExercisePersistenceModel>> GetAccountExercisesAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => ReadAsync(dbContext.Exercises.Where(exercise => exercise.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId) && !exercise.IsDeleted), cancellationToken);

    public Task<IReadOnlyList<WorkoutExercisePersistenceModel>> GetByBodyPartAsync(Id<AccountReference> accountId, BodyParts bodyPart, CancellationToken cancellationToken = default)
    {
        var persistedAccountId = WorkoutPersistenceAccountIds.ToPersisted(accountId);
        return ReadAsync(dbContext.Exercises.Where(exercise => exercise.BodyPart == bodyPart && !exercise.IsDeleted && (exercise.UserId == persistedAccountId || exercise.UserId == null)), cancellationToken);
    }

    public Task<IReadOnlyList<WorkoutExercisePersistenceModel>> GetByIdsAsync(IReadOnlyCollection<Id<Exercise>> ids, CancellationToken cancellationToken = default)
        => ReadAsync(dbContext.Exercises.Where(exercise => ids.Contains(exercise.Id)), cancellationToken);

    public async Task<IReadOnlyDictionary<Id<Exercise>, string>> GetTranslationsAsync(IReadOnlyCollection<Id<Exercise>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
    {
        var ids = exerciseIds.Distinct().ToList();
        var normalizedCultures = cultures.Select(culture => culture.Trim().ToLowerInvariant()).Where(culture => culture.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0 || normalizedCultures.Count == 0) return new Dictionary<Id<Exercise>, string>();
        var cultureIndex = normalizedCultures.Select((culture, index) => (culture, index)).ToDictionary(item => item.culture, item => item.index, StringComparer.Ordinal);
        var translations = await dbContext.ExerciseTranslations.AsNoTracking()
            .Where(translation => ids.Contains(translation.ExerciseId) && normalizedCultures.Contains(translation.Culture))
            .Select(translation => new { translation.ExerciseId, translation.Culture, translation.Name })
            .ToListAsync(cancellationToken);
        return translations.OrderBy(translation => cultureIndex.GetValueOrDefault(translation.Culture, int.MaxValue)).GroupBy(translation => translation.ExerciseId).ToDictionary(group => group.Key, group => group.First().Name);
    }

    public async Task UpsertTranslationAsync(Id<Exercise> exerciseId, string culture, string name, CancellationToken cancellationToken = default)
    {
        var normalizedCulture = culture.Trim().ToLowerInvariant();
        var translation = await dbContext.ExerciseTranslations.FirstOrDefaultAsync(item => item.ExerciseId == exerciseId && item.Culture == normalizedCulture, cancellationToken);
        if (translation is null)
        {
            await dbContext.ExerciseTranslations.AddAsync(new ExerciseTranslation { Id = Id<ExerciseTranslation>.New(), ExerciseId = exerciseId, Culture = normalizedCulture, Name = name.Trim() }, cancellationToken);
            return;
        }
        translation.Name = name.Trim();
    }

    public Task AddAsync(WorkoutExerciseWriteModel exercise, CancellationToken cancellationToken = default)
        => dbContext.Exercises.AddAsync(ToEntity(exercise), cancellationToken).AsTask();

    public async Task UpdateAsync(WorkoutExerciseWriteModel exercise, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Exercises.FirstAsync(item => item.Id == exercise.Id, cancellationToken);
        entity.UserId = exercise.OwnerId.HasValue ? WorkoutPersistenceAccountIds.ToPersisted(exercise.OwnerId.Value) : null;
        entity.Name = exercise.Name;
        entity.BodyPart = exercise.BodyPart;
        entity.EloFormula = exercise.EloFormula;
        entity.Description = exercise.Description;
        entity.Image = exercise.Image;
        entity.IsDeleted = exercise.IsDeleted;
    }

    private static Exercise ToEntity(WorkoutExerciseWriteModel model) => new()
    {
        Id = model.Id,
        UserId = model.OwnerId.HasValue ? WorkoutPersistenceAccountIds.ToPersisted(model.OwnerId.Value) : null,
        Name = model.Name,
        BodyPart = model.BodyPart,
        EloFormula = model.EloFormula,
        Description = model.Description,
        Image = model.Image,
        IsDeleted = model.IsDeleted
    };

    private static async Task<IReadOnlyList<WorkoutExercisePersistenceModel>> ReadAsync(IQueryable<Exercise> query, CancellationToken cancellationToken)
        => (await query.AsNoTracking().ToListAsync(cancellationToken)).Select(WorkoutPersistenceProjection.Exercise).ToList();

    private static async Task<WorkoutExercisePersistenceModel?> FindAsync(IQueryable<Exercise> query, CancellationToken cancellationToken)
    {
        var entity = await query.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : WorkoutPersistenceProjection.Exercise(entity);
    }
}
