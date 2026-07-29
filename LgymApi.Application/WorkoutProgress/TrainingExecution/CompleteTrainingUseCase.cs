using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.Application.WorkoutProgress.Scoring.Elo;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

internal sealed class CompleteTrainingUseCase : ICompleteTrainingUseCase
{
    private readonly ICompleteTrainingUseCaseDependencies _dependencies;
    private readonly Dictionary<ExerciseEloFormula, IExerciseEloCalculator> _exerciseEloCalculators;

    public CompleteTrainingUseCase(ICompleteTrainingUseCaseDependencies dependencies)
    {
        _dependencies = dependencies;
        _exerciseEloCalculators = dependencies.ExerciseEloCalculators.ToDictionary(calculator => calculator.Formula);
    }

    public async Task<Result<TrainingSummaryResult, AppError>> AddTrainingAsync(
        Id<AccountReference> userId,
        CompleteTrainingInput input,
        CancellationToken cancellationToken = default)
    {
        var (gymId, planDayId, createdAt, exercises) = input;
        if (userId.IsEmpty || gymId.IsEmpty || planDayId.IsEmpty)
        {
            return Result<TrainingSummaryResult, AppError>.Failure(new InvalidTrainingDataError(Messages.InvalidId));
        }

        if (await _dependencies.AccountAccess.GetByIdAsync(userId, cancellationToken) is null)
        {
            return Result<TrainingSummaryResult, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var gym = await _dependencies.GymRepository.FindByIdAsync(gymId, cancellationToken);
        if (gym == null)
        {
            return Result<TrainingSummaryResult, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var planDay = await _dependencies.PlanDayReferences.GetByIdAsync(planDayId, cancellationToken);
        if (!planDay.Exists || planDay.IsDeleted)
        {
            return Result<TrainingSummaryResult, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var uniqueExerciseIds = exercises
            .Select(exercise => exercise.ExerciseId)
            .Where(exerciseId => !exerciseId.IsEmpty)
            .Distinct()
            .ToList();
        var exerciseDetails = await _dependencies.ExerciseRepository.GetByIdsAsync(uniqueExerciseIds, cancellationToken);
        var exerciseDetailsMap = exerciseDetails.ToDictionary(exercise => exercise.Id, exercise => exercise.Name);
        var exerciseFormulaMap = exerciseDetails.ToDictionary(exercise => exercise.Id, exercise => exercise.EloFormula);
        var previousScoresMap = await FetchPreviousScoresAsync(userId, gym.Id, uniqueExerciseIds, cancellationToken);

        await using var transaction = await _dependencies.UnitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var createdAtUtc = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc);
            var training = new WorkoutTrainingWriteModel(Id<Training>.New(), userId, planDayId, gym.Id, new DateTimeOffset(createdAtUtc));
            await _dependencies.TrainingRepository.AddAsync(training, cancellationToken);

            var savedScoreIds = new List<Id<ExerciseScore>>();
            var totalElo = 0;
            var scoresToAdd = new List<WorkoutExerciseScoreWriteModel>();
            var index = 0;
            foreach (var exercise in exercises)
            {
                if (exercise.ExerciseId.IsEmpty)
                {
                    continue;
                }

                if (exercise.Unit == WeightUnits.Unknown)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    return Result<TrainingSummaryResult, AppError>.Failure(new InvalidTrainingDataError(Messages.FieldRequired));
                }

                var scoreEntity = new WorkoutExerciseScoreWriteModel(Id<ExerciseScore>.New(), exercise.ExerciseId, userId, exercise.Reps, exercise.Series, exercise.Weight, exercise.Unit, training.Id, index);
                scoresToAdd.Add(scoreEntity);
                savedScoreIds.Add(scoreEntity.Id);
                index++;

                var key = $"{exercise.ExerciseId}-{exercise.Series}";
                if (previousScoresMap.TryGetValue(key, out var previousScore))
                {
                    var formula = exerciseFormulaMap.TryGetValue(exercise.ExerciseId, out var exerciseFormula)
                        ? exerciseFormula
                        : ExerciseEloFormula.Standard;
                    totalElo += CalculateEloPerExercise(new ExerciseEloCalculationInput(
                        previousScore.Weight,
                        previousScore.Reps,
                        scoreEntity.Weight,
                        scoreEntity.Reps), formula);
                }
            }

            if (scoresToAdd.Count > 0)
            {
                await _dependencies.ExerciseScoreRepository.AddRangeAsync(scoresToAdd, cancellationToken);
            }

            var trainingScores = savedScoreIds.Select((scoreId, scoreIndex) => new WorkoutTrainingExerciseScorePersistenceModel(Id<TrainingExerciseScore>.New(), training.Id, scoreId, scoreIndex)).ToList();
            if (trainingScores.Count > 0)
            {
                await _dependencies.TrainingRepository.AddExerciseScoreLinksAsync(trainingScores, cancellationToken);
            }

            var eloEntry = await _dependencies.EloRepository.GetLatestEntryAsync(userId, cancellationToken);
            if (eloEntry == null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Result<TrainingSummaryResult, AppError>.Failure(new InternalServerError(Messages.TryAgain));
            }

            var newElo = totalElo + eloEntry.Elo;
            var currentRank = _dependencies.RankService.GetCurrentRank(newElo);
            var nextRank = _dependencies.RankService.GetNextRank(currentRank.Name);
            await _dependencies.EloRepository.AddAsync(new WorkoutEloWriteModel(Id<EloRegistry>.New(), userId, DateTimeOffset.UtcNow, newElo, training.Id), cancellationToken);
            await _dependencies.TrainingRepository.UpdateAccountProfileRankAsync(userId, currentRank.Name, cancellationToken);
            await _dependencies.TrainingRepository.StageTrainingCompletedCommandAsync(userId, training.Id, cancellationToken);
            await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken);

            var comparison = TrainingComparisonReportBuilder.Build(exercises, previousScoresMap, exerciseDetailsMap);
            await transaction.CommitAsync(cancellationToken);
            return Result<TrainingSummaryResult, AppError>.Success(new TrainingSummaryResult
            {
                Comparison = comparison,
                GainElo = totalElo,
                UserOldElo = eloEntry.Elo,
                ProfileRank = new Features.User.Models.RankInfo { Name = currentRank.Name, NeedElo = currentRank.NeedElo },
                NextRank = nextRank == null ? null : new Features.User.Models.RankInfo { Name = nextRank.Name, NeedElo = nextRank.NeedElo },
                Message = Messages.Created
            });
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private int CalculateEloPerExercise(ExerciseEloCalculationInput input, ExerciseEloFormula formula)
    {
        if (!_exerciseEloCalculators.TryGetValue(formula, out var calculator))
        {
            calculator = _exerciseEloCalculators[ExerciseEloFormula.Standard];
        }

        return calculator.Calculate(input);
    }

    private async Task<Dictionary<string, WorkoutExerciseScorePersistenceModel>> FetchPreviousScoresAsync(
        Id<AccountReference> userId,
        Id<Gym> gymId,
        List<Id<Exercise>> exerciseIds,
        CancellationToken cancellationToken)
    {
        var scores = await _dependencies.ExerciseScoreRepository.GetByAccountAndExercisesAsync(userId, exerciseIds, cancellationToken);
        scores = scores
            .Where(score => score.Training != null && score.Training.GymId == gymId)
            .OrderByDescending(score => score.CreatedAt)
            .ToList();

        var map = new Dictionary<string, WorkoutExerciseScorePersistenceModel>();
        foreach (var score in scores)
        {
            var key = $"{score.ExerciseId}-{score.Series}";
            if (!map.ContainsKey(key))
            {
                map[key] = score;
            }
        }

        return map;
    }
}
