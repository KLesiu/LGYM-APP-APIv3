using System.Globalization;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;

namespace LgymApi.Application.WorkoutProgress.ProgressData;

public sealed partial class WorkoutProgressReadWriteService : IWorkoutProgressReadWriteService
{
    private readonly WorkoutProgressReadWriteServiceDependencies _dependencies;

    public WorkoutProgressReadWriteService(WorkoutProgressReadWriteServiceDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    public async Task<Result<List<ExerciseScoreChartPoint>, AppError>> GetExerciseScoreChartAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty || exerciseId.IsEmpty)
        {
            return Result<List<ExerciseScoreChartPoint>, AppError>.Failure(new InvalidExerciseScoreError(Messages.InvalidId));
        }

        if (await _dependencies.AccountAccess.GetByIdAsync(userId, cancellationToken) is null)
        {
            return Result<List<ExerciseScoreChartPoint>, AppError>.Failure(new ExerciseScoreNotFoundError(Messages.DidntFind));
        }

        var scores = await _dependencies.ExerciseScoreRepository.GetByAccountAndExerciseAsync(userId, exerciseId, cancellationToken);
        var bestSeries = new Dictionary<string, ExerciseScoreChartPoint>();
        foreach (var score in scores.OrderBy(score => score.CreatedAt))
        {
            if (score.Training == null || score.Exercise == null)
            {
                continue;
            }

            var key = $"{score.ExerciseId}-{score.TrainingId}";
            var point = new ExerciseScoreChartPoint(key, CalculateOneRepMax(score.Reps, score.Weight), score.Training.CreatedAt.UtcDateTime.ToString("MM/dd", CultureInfo.InvariantCulture), score.Exercise.Name, score.ExerciseId);
            if (!bestSeries.TryGetValue(key, out var current) || point.Value > current.Value)
            {
                bestSeries[key] = point;
            }
        }

        return Result<List<ExerciseScoreChartPoint>, AppError>.Success(bestSeries.Values.ToList());
    }

    public async Task<Result<List<EloChartPoint>, AppError>> GetEloChartAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<List<EloChartPoint>, AppError>.Failure(new InvalidEloRegistryError(Messages.InvalidId));
        }

        var entries = await _dependencies.EloRegistryRepository.GetByAccountIdAsync(userId, cancellationToken);
        return entries.Count == 0
            ? Result<List<EloChartPoint>, AppError>.Failure(new EloRegistryNotFoundError(Messages.DidntFind))
            : Result<List<EloChartPoint>, AppError>.Success(entries.Select(entry => new EloChartPoint(entry.Id, entry.Elo, entry.Date.UtcDateTime.ToString("MM/dd", CultureInfo.InvariantCulture))).ToList());
    }

    public async Task<Result<int, AppError>> GetLatestEloAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty)
        {
            return Result<int, AppError>.Failure(new InvalidUserError(Messages.DidntFind));
        }

        var elo = await _dependencies.EloRegistryRepository.GetLatestEloAsync(userId, cancellationToken);
        return elo.HasValue ? Result<int, AppError>.Success(elo.Value) : Result<int, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
    }

    public async Task<int> GetLatestEloOrDefaultAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
        => await _dependencies.EloRegistryRepository.GetLatestEloAsync(userId, cancellationToken) ?? 1000;

    public Task InitializeEloAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
        => _dependencies.EloRegistryRepository.CreateInitialForAccountAsync(userId, cancellationToken);

    private static double CalculateOneRepMax(double reps, double weight)
    {
        if (reps <= 0 || weight <= 0) return 0;
        var epley = weight * (1 + reps / 30d);
        var brzycki = weight * (36d / (37d - reps));
        var lander = weight * (100d / (101.3d - 2.67123d * reps));
        var lombardi = weight * Math.Pow(reps, 0.1d);
        return Math.Round((epley + brzycki + lander + lombardi) / 4d, 0);
    }
}
