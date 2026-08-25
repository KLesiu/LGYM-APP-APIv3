using System.Globalization;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.Repositories;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts.Accounts;
using LgymApi.Resources;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;

namespace LgymApi.Application.WorkoutProgress.ProgressData;

public sealed partial class WorkoutProgressReadWriteService : IWorkoutProgressReadWriteService
{
    public async Task<IReadOnlyDictionary<Id<ExerciseEntity>, string>> GetExerciseDisplayNamesAsync(
        IEnumerable<Id<ExerciseEntity>> exerciseIds,
        IReadOnlyList<string> cultures,
        CancellationToken cancellationToken = default)
    {
        var ids = exerciseIds.Where(id => !id.IsEmpty).Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<Id<ExerciseEntity>, string>()
            : await _exerciseRepository.GetTranslationsAsync(ids, cultures, cancellationToken);
    }

    private readonly IWorkoutExercisePersistence _exerciseRepository;
    private readonly IWorkoutExerciseScorePersistence _exerciseScoreRepository;
    private readonly IWorkoutMeasurementPersistence _measurementRepository;
    private readonly IWorkoutMainRecordPersistence _mainRecordRepository;
    private readonly IWorkoutEloPersistence _eloRegistryRepository;
    private readonly IAccountAccessReader _accountAccess;
    private readonly IUnitConverter<HeightUnits> _heightUnitConverter;
    private readonly IUnitConverter<WeightUnits> _weightUnitConverter;
    private readonly IUnitOfWork _unitOfWork;

    public WorkoutProgressReadWriteService(
        IWorkoutExercisePersistence exerciseRepository,
        IWorkoutExerciseScorePersistence exerciseScoreRepository,
        IWorkoutMeasurementPersistence measurementRepository,
        IWorkoutMainRecordPersistence mainRecordRepository,
        IWorkoutEloPersistence eloRegistryRepository,
        IAccountAccessReader accountAccess,
        IUnitConverter<HeightUnits> heightUnitConverter,
        IUnitConverter<WeightUnits> weightUnitConverter,
        IUnitOfWork unitOfWork)
    {
        _exerciseRepository = exerciseRepository;
        _exerciseScoreRepository = exerciseScoreRepository;
        _measurementRepository = measurementRepository;
        _mainRecordRepository = mainRecordRepository;
        _eloRegistryRepository = eloRegistryRepository;
        _accountAccess = accountAccess;
        _heightUnitConverter = heightUnitConverter;
        _weightUnitConverter = weightUnitConverter;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<List<ExerciseScoreChartPoint>, AppError>> GetExerciseScoreChartAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty || exerciseId.IsEmpty)
        {
            return Result<List<ExerciseScoreChartPoint>, AppError>.Failure(new InvalidExerciseScoreError(Messages.InvalidId));
        }

        if (await _accountAccess.GetByIdAsync(userId, cancellationToken) is null)
        {
            return Result<List<ExerciseScoreChartPoint>, AppError>.Failure(new ExerciseScoreNotFoundError(Messages.DidntFind));
        }

        var scores = await _exerciseScoreRepository.GetByAccountAndExerciseAsync(userId, exerciseId, cancellationToken);
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

        var entries = await _eloRegistryRepository.GetByAccountIdAsync(userId, cancellationToken);
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

        var elo = await _eloRegistryRepository.GetLatestEloAsync(userId, cancellationToken);
        return elo.HasValue ? Result<int, AppError>.Success(elo.Value) : Result<int, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
    }

    public async Task<int> GetLatestEloOrDefaultAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
        => await _eloRegistryRepository.GetLatestEloAsync(userId, cancellationToken) ?? 1000;

    public Task InitializeEloAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
        => _eloRegistryRepository.CreateInitialForAccountAsync(userId, cancellationToken);

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
