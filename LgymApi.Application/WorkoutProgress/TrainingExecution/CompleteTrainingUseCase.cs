using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.Scoring.Elo;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

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
        Id<User> userId,
        CompleteTrainingInput input,
        CancellationToken cancellationToken = default)
    {
        var (gymId, planDayId, createdAt, exercises) = input;
        if (userId.IsEmpty || gymId.IsEmpty || planDayId.IsEmpty)
        {
            return Result<TrainingSummaryResult, AppError>.Failure(new InvalidTrainingDataError(Messages.InvalidId));
        }

        var user = await _dependencies.UserRepository.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return Result<TrainingSummaryResult, AppError>.Failure(new TrainingNotFoundError(Messages.DidntFind));
        }

        var gym = await _dependencies.GymRepository.FindByIdAsync(gymId, cancellationToken);
        if (gym == null)
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
        var previousScoresMap = await FetchPreviousScoresAsync(user.Id, gym.Id, uniqueExerciseIds, cancellationToken);

        await using var transaction = await _dependencies.UnitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var createdAtUtc = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc);
            var training = new Training
            {
                Id = Id<Training>.New(),
                UserId = user.Id,
                TypePlanDayId = planDayId,
                CreatedAt = new DateTimeOffset(createdAtUtc),
                GymId = gym.Id
            };
            await _dependencies.TrainingRepository.AddAsync(training, cancellationToken);

            var savedScoreIds = new List<Id<ExerciseScore>>();
            var totalElo = 0;
            var scoresToAdd = new List<ExerciseScore>();
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

                var scoreEntity = new ExerciseScore
                {
                    Id = Id<ExerciseScore>.New(),
                    ExerciseId = exercise.ExerciseId,
                    UserId = user.Id,
                    Reps = exercise.Reps,
                    Series = exercise.Series,
                    Weight = new Weight(exercise.Weight, exercise.Unit),
                    TrainingId = training.Id,
                    Order = index
                };
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
                        previousScore.Weight.Value,
                        previousScore.Reps,
                        scoreEntity.Weight.Value,
                        scoreEntity.Reps), formula);
                }
            }

            if (scoresToAdd.Count > 0)
            {
                await _dependencies.ExerciseScoreRepository.AddRangeAsync(scoresToAdd, cancellationToken);
            }

            var trainingScores = savedScoreIds.Select((scoreId, scoreIndex) => new TrainingExerciseScore
            {
                Id = Id<TrainingExerciseScore>.New(),
                TrainingId = training.Id,
                ExerciseScoreId = scoreId,
                Order = scoreIndex
            }).ToList();
            if (trainingScores.Count > 0)
            {
                await _dependencies.TrainingExerciseScoreRepository.AddRangeAsync(trainingScores, cancellationToken);
            }

            var eloEntry = await _dependencies.EloRepository.GetLatestEntryAsync(user.Id, cancellationToken);
            if (eloEntry == null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Result<TrainingSummaryResult, AppError>.Failure(new InternalServerError(Messages.TryAgain));
            }

            var newElo = totalElo + eloEntry.Elo;
            var currentRank = _dependencies.RankService.GetCurrentRank(newElo);
            var nextRank = _dependencies.RankService.GetNextRank(currentRank.Name);
            user.ProfileRank = currentRank.Name;
            await _dependencies.EloRepository.AddAsync(new EloRegistry
            {
                Id = Id<EloRegistry>.New(),
                UserId = user.Id,
                Date = DateTimeOffset.UtcNow,
                Elo = newElo,
                TrainingId = training.Id
            }, cancellationToken);
            await _dependencies.UserRepository.UpdateAsync(user, cancellationToken);
            await _dependencies.CommandDispatcher.EnqueueAsync(new TrainingCompletedCommand { UserId = user.Id, TrainingId = training.Id });
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

    private async Task<Dictionary<string, ExerciseScore>> FetchPreviousScoresAsync(
        Id<User> userId,
        Id<Gym> gymId,
        List<Id<Exercise>> exerciseIds,
        CancellationToken cancellationToken)
    {
        var scores = await _dependencies.ExerciseScoreRepository.GetByUserAndExercisesAsync(userId, exerciseIds, cancellationToken);
        scores = scores
            .Where(score => score.Training != null && score.Training.GymId == gymId)
            .OrderByDescending(score => score.CreatedAt)
            .ToList();

        var map = new Dictionary<string, ExerciseScore>();
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
