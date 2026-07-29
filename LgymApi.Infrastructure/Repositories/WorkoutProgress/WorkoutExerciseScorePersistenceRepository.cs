using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.WorkoutProgress;

public sealed class WorkoutExerciseScorePersistenceRepository(AppDbContext dbContext) : IWorkoutExerciseScorePersistence
{
    public Task AddRangeAsync(IReadOnlyCollection<WorkoutExerciseScoreWriteModel> scores, CancellationToken cancellationToken = default)
        => dbContext.ExerciseScores.AddRangeAsync(scores.Select(ToEntity), cancellationToken);

    public Task<IReadOnlyList<WorkoutExerciseScorePersistenceModel>> GetByIdsAsync(IReadOnlyCollection<Id<ExerciseScore>> ids, CancellationToken cancellationToken = default)
        => ReadAsync(dbContext.ExerciseScores.Include(score => score.Exercise).Where(score => ids.Contains(score.Id)), cancellationToken);

    public Task<IReadOnlyList<WorkoutExerciseScorePersistenceModel>> GetByAccountAndExerciseAsync(Id<AccountReference> accountId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default)
        => ReadAsync(WithHistory().Where(score => score.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId) && score.ExerciseId == exerciseId).OrderByDescending(score => score.CreatedAt), cancellationToken);

    public Task<IReadOnlyList<WorkoutExerciseScorePersistenceModel>> GetByAccountAndExercisesAsync(Id<AccountReference> accountId, IReadOnlyCollection<Id<Exercise>> exerciseIds, CancellationToken cancellationToken = default)
        => ReadAsync(dbContext.ExerciseScores.Include(score => score.Training).Where(score => score.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId) && exerciseIds.Contains(score.ExerciseId)).OrderByDescending(score => score.CreatedAt), cancellationToken);

    public Task<IReadOnlyList<WorkoutExerciseScorePersistenceModel>> GetLatestByAccountExerciseSeriesAsync(Id<AccountReference> accountId, Id<Exercise> exerciseId, Id<Gym>? gymId, CancellationToken cancellationToken = default)
    {
        var persistedAccountId = WorkoutPersistenceAccountIds.ToPersisted(accountId);
        var filtered = dbContext.ExerciseScores.Where(score => score.UserId == persistedAccountId && score.ExerciseId == exerciseId);
        if (gymId.HasValue) filtered = filtered.Where(score => score.Training != null && score.Training.GymId == gymId.Value);
        var latestIds = filtered.GroupBy(score => score.Series).Select(group => group.OrderByDescending(score => score.CreatedAt).Select(score => score.Id).First());
        return ReadAsync(dbContext.ExerciseScores.Include(score => score.Training).ThenInclude(training => training!.Gym).Where(score => latestIds.Contains(score.Id)).OrderBy(score => score.Series), cancellationToken);
    }

    public async Task<WorkoutExerciseScorePersistenceModel?> GetBestScoreAsync(Id<AccountReference> accountId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ExerciseScores.AsNoTracking().Where(score => score.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId) && score.ExerciseId == exerciseId).OrderByDescending(score => score.WeightValue).ThenByDescending(score => score.Reps).FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : WorkoutPersistenceProjection.ExerciseScore(entity);
    }

    private IQueryable<ExerciseScore> WithHistory() => dbContext.ExerciseScores.Include(score => score.Exercise).Include(score => score.Training).ThenInclude(training => training!.Gym);

    private static async Task<IReadOnlyList<WorkoutExerciseScorePersistenceModel>> ReadAsync(IQueryable<ExerciseScore> query, CancellationToken cancellationToken)
        => (await query.AsNoTracking().ToListAsync(cancellationToken)).Select(WorkoutPersistenceProjection.ExerciseScore).ToList();

    private static ExerciseScore ToEntity(WorkoutExerciseScoreWriteModel model) => new()
    {
        Id = model.Id,
        ExerciseId = model.ExerciseId,
        UserId = WorkoutPersistenceAccountIds.ToPersisted(model.AccountId),
        Reps = model.Reps,
        Series = model.Series,
        Weight = new Weight(model.Weight, model.Unit),
        TrainingId = model.TrainingId,
        Order = model.Order
    };
}
