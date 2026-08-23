using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;

namespace LgymApi.Application.Features.Exercise;

public sealed partial class ExerciseService : IExerciseService
{
    public async Task<Result<LastExerciseScoresResult, AppError>> GetLastExerciseScoresAsync(GetLastExerciseScoresInput input, CancellationToken cancellationToken = default)
    {
        var (routeUserId, currentUserId, exerciseId, series, gymId, exerciseName) = input;

        if (routeUserId.IsEmpty || currentUserId.IsEmpty || exerciseId.IsEmpty)
        {
            return Result<LastExerciseScoresResult, AppError>.Failure(new InvalidExerciseError(Messages.InvalidId));
        }

        if (routeUserId != currentUserId)
        {
            return Result<LastExerciseScoresResult, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var latestScores = await _exerciseScoreRepository.GetLatestByAccountExerciseSeriesAsync(
            currentUserId,
            exerciseId,
            gymId,
            cancellationToken);
        var latestBySeries = latestScores.ToDictionary(s => s.Series, s => s);

        var safeSeriesLimit = Math.Clamp(series, 1, ExerciseLimits.MaxSeries);

        var seriesScores = new List<SeriesScoreResult>(safeSeriesLimit);
        for (var i = 1; i <= safeSeriesLimit; i++)
        {
            latestBySeries.TryGetValue(i, out var score);
            seriesScores.Add(new SeriesScoreResult
            {
                Series = i,
            Score = score == null ? null : MapScore(score)
            });
        }

        return Result<LastExerciseScoresResult, AppError>.Success(new LastExerciseScoresResult
        {
            ExerciseId = exerciseId,
            ExerciseName = exerciseName,
            SeriesScores = seriesScores
        });
    }

    public async Task<Result<List<ExerciseTrainingHistoryItem>, AppError>> GetExerciseScoresFromTrainingByExerciseAsync(Id<AccountReference> currentUserId, Id<Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default)
    {
        if (currentUserId.IsEmpty || exerciseId.IsEmpty)
        {
            return Result<List<ExerciseTrainingHistoryItem>, AppError>.Failure(new InvalidExerciseError(Messages.FieldRequired));
        }

        var exercise = await _exerciseRepository.FindVisibleToAccountAsync(exerciseId, currentUserId, cancellationToken);
        if (exercise == null)
        {
            return Result<List<ExerciseTrainingHistoryItem>, AppError>.Failure(new ExerciseNotFoundError(Messages.DidntFind));
        }

        var scores = await _exerciseScoreRepository.GetByAccountAndExerciseAsync(currentUserId, exerciseId, cancellationToken);
        var planDays = await _planDayReferences.GetByIdsAsync(
            scores
                .Where(score => score.Training?.Gym != null)
                .Select(score => score.Training!.TypePlanDayId)
                .Distinct()
                .ToList(),
            cancellationToken);
        var planDaysById = planDays.ToDictionary(planDay => planDay.PlanDayId);

        var tempMap = new Dictionary<Id<LgymApi.Domain.Entities.Training>, (DateTimeOffset Date, string GymName, string TrainingName, List<(int Series, WorkoutProgress.Persistence.WorkoutExerciseScorePersistenceModel Score)> RawScores, int MaxSeries)>();
        foreach (var score in scores)
        {
            if (score.Training?.Gym == null)
            {
                continue;
            }

            var planDay = planDaysById[score.Training.TypePlanDayId];

            var trainingId = score.Training.Id;
            if (!tempMap.TryGetValue(trainingId, out var entry))
            {
                entry = (score.Training.CreatedAt, score.Training.Gym.Name, planDay.Name, new List<(int, WorkoutProgress.Persistence.WorkoutExerciseScorePersistenceModel)>(), 0);
            }

            entry.RawScores.Add((score.Series, score));
            entry.MaxSeries = Math.Max(entry.MaxSeries, score.Series);
            tempMap[trainingId] = entry;
        }

        var result = new List<ExerciseTrainingHistoryItem>();
        foreach (var (trainingId, entry) in tempMap)
        {
            var seriesScores = new List<SeriesScoreResult>();
            var scoreMap = entry.RawScores
                .GroupBy(s => s.Series)
                .ToDictionary(g => g.Key, g => g.First().Score);

            for (var i = 1; i <= entry.MaxSeries; i++)
            {
                scoreMap.TryGetValue(i, out var score);
                seriesScores.Add(new SeriesScoreResult { Series = i, Score = score == null ? null : MapScore(score) });
            }

            result.Add(new ExerciseTrainingHistoryItem
            {
                Id = trainingId,
                Date = entry.Date.UtcDateTime,
                GymName = entry.GymName,
                TrainingName = entry.TrainingName,
                SeriesScores = seriesScores
            });
        }

        return Result<List<ExerciseTrainingHistoryItem>, AppError>.Success(result);
    }
}
