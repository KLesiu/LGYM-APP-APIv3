using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.WorkoutProgress;

public sealed class WorkoutTrainingPersistenceRepository(AppDbContext dbContext, ICommandDispatcher commandDispatcher) : IWorkoutTrainingPersistence
{
    public Task AddAsync(WorkoutTrainingWriteModel training, CancellationToken cancellationToken = default)
        => dbContext.Trainings.AddAsync(new Training { Id = training.Id, UserId = WorkoutPersistenceAccountIds.ToPersisted(training.AccountId), TypePlanDayId = training.TypePlanDayId.Rebind<PlanDay>(), GymId = training.GymId, CreatedAt = training.CreatedAt }, cancellationToken).AsTask();

    public async Task<WorkoutTrainingPersistenceModel?> GetLastByAccountIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
    {
        var entity = await WithDetails().OrderByDescending(training => training.CreatedAt).FirstOrDefaultAsync(training => training.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId), cancellationToken);
        return entity is null ? null : WorkoutPersistenceProjection.Training(entity);
    }

    public async Task<IReadOnlyList<WorkoutTrainingPersistenceModel>> GetByAccountIdAndDateAsync(Id<AccountReference> accountId, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken = default)
        => (await WithDetails().Where(training => training.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId) && training.CreatedAt >= start && training.CreatedAt <= end).ToListAsync(cancellationToken)).Select(WorkoutPersistenceProjection.Training).ToList();

    public async Task<IReadOnlyList<DateTimeOffset>> GetDatesByAccountIdAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => await dbContext.Trainings.AsNoTracking().Where(training => training.UserId == WorkoutPersistenceAccountIds.ToPersisted(accountId)).OrderBy(training => training.CreatedAt).Select(training => training.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Id<PlanDayReference>, DateTime?>> GetLastTrainingDatesAsync(
        IReadOnlyCollection<Id<PlanDayReference>> planDayIds,
        CancellationToken cancellationToken = default)
    {
        var requestedIds = planDayIds.Distinct().ToArray();
        var result = requestedIds.ToDictionary(id => id, _ => (DateTime?)null);
        if (requestedIds.Length == 0) return result;
        var persistedPlanDayIds = requestedIds.Select(id => id.Rebind<PlanDay>()).ToArray();

        var trainings = await dbContext.Trainings.AsNoTracking()
            .Where(training => persistedPlanDayIds.Contains(training.TypePlanDayId))
            .ToListAsync(cancellationToken);
        foreach (var group in trainings.GroupBy(training => training.TypePlanDayId.Rebind<PlanDayReference>()))
        {
            result[group.Key] = group.Max(training => training.CreatedAt).UtcDateTime;
        }

        return result;
    }

    public async Task<IReadOnlyList<WorkoutTrainingPersistenceModel>> GetByGymIdsAsync(IReadOnlyCollection<Id<Gym>> gymIds, CancellationToken cancellationToken = default)
        => (await WithDetails().Where(training => gymIds.Contains(training.GymId)).ToListAsync(cancellationToken)).Select(WorkoutPersistenceProjection.Training).ToList();

    public Task AddExerciseScoreLinksAsync(IReadOnlyCollection<WorkoutTrainingExerciseScorePersistenceModel> links, CancellationToken cancellationToken = default)
        => dbContext.TrainingExerciseScores.AddRangeAsync(links.Select(link => new TrainingExerciseScore { Id = link.Id, TrainingId = link.TrainingId, ExerciseScoreId = link.ExerciseScoreId, Order = link.Order }), cancellationToken);

    public async Task<IReadOnlyList<WorkoutTrainingExerciseScorePersistenceModel>> GetExerciseScoreLinksAsync(IReadOnlyCollection<Id<Training>> trainingIds, CancellationToken cancellationToken = default)
        => await dbContext.TrainingExerciseScores.AsNoTracking().Where(link => trainingIds.Contains(link.TrainingId)).OrderBy(link => link.TrainingId).ThenBy(link => link.Order).ThenBy(link => link.Id).Select(link => new WorkoutTrainingExerciseScorePersistenceModel(link.Id, link.TrainingId, link.ExerciseScoreId, link.Order)).ToListAsync(cancellationToken);

    public async Task UpdateAccountProfileRankAsync(Id<AccountReference> accountId, string profileRank, CancellationToken cancellationToken = default)
        => (await dbContext.Users.FirstAsync(user => user.Id == WorkoutPersistenceAccountIds.ToPersisted(accountId), cancellationToken)).ProfileRank = profileRank;

    public Task StageTrainingCompletedCommandAsync(Id<AccountReference> accountId, Id<Training> trainingId, CancellationToken cancellationToken = default)
        => commandDispatcher.EnqueueAsync(new TrainingCompletedCommand { UserId = WorkoutPersistenceAccountIds.ToPersisted(accountId), TrainingId = trainingId });

    private IQueryable<Training> WithDetails() => dbContext.Trainings.AsNoTracking().Include(training => training.Gym);
}
