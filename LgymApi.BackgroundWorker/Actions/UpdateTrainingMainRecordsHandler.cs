using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Common;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using MainRecordEntity = LgymApi.Domain.Entities.MainRecord;

namespace LgymApi.BackgroundWorker.Actions;

/// <summary>
/// Background action handler that updates main records after training completion.
/// Analyzes training exercises and creates new personal records when weights exceed previous bests.
/// Fetches exercise data from repositories instead of command payload.
/// </summary>
public sealed partial class UpdateTrainingMainRecordsHandler : global::LgymApi.BackgroundWorker.Actions.Contracts.IBackgroundAction<TrainingCompletedCommand>
{
    private readonly IMainRecordRepository _mainRecordRepository;
    private readonly ITrainingRepository _trainingRepository;
    private readonly ITrainingExerciseScoreRepository _trainingExerciseScoreRepository;
    private readonly IExerciseScoreRepository _exerciseScoreRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUnitConverter<WeightUnits> _weightUnitConverter;
    private readonly ILogger<UpdateTrainingMainRecordsHandler> _logger;

    public UpdateTrainingMainRecordsHandler(
        IMainRecordRepository mainRecordRepository,
        ITrainingRepository trainingRepository,
        ITrainingExerciseScoreRepository trainingExerciseScoreRepository,
        IExerciseScoreRepository exerciseScoreRepository,
        IUnitOfWork unitOfWork,
        IUnitConverter<WeightUnits> weightUnitConverter,
        ILogger<UpdateTrainingMainRecordsHandler> logger)
    {
        _mainRecordRepository = mainRecordRepository ?? throw new ArgumentNullException(nameof(mainRecordRepository));
        _trainingRepository = trainingRepository ?? throw new ArgumentNullException(nameof(trainingRepository));
        _trainingExerciseScoreRepository = trainingExerciseScoreRepository ?? throw new ArgumentNullException(nameof(trainingExerciseScoreRepository));
        _exerciseScoreRepository = exerciseScoreRepository ?? throw new ArgumentNullException(nameof(exerciseScoreRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _weightUnitConverter = weightUnitConverter ?? throw new ArgumentNullException(nameof(weightUnitConverter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(TrainingCompletedCommand command, CancellationToken cancellationToken = default)
    {
        // Fetch training entity to get date
        var training = await _trainingRepository.GetByIdAsync((Domain.ValueObjects.Id<Domain.Entities.Training>)command.TrainingId, cancellationToken);
        if (training is null)
        {
            _logger.LogWarning(
                "Training {TrainingId} not found - cannot update main records",
                command.TrainingId);
            return;
        }

        // Fetch exercise data from repositories
        var trainingExerciseScores = await _trainingExerciseScoreRepository.GetByTrainingIdsAsync(
            new List<Domain.ValueObjects.Id<Domain.Entities.Training>> { (Domain.ValueObjects.Id<Domain.Entities.Training>)command.TrainingId },
            cancellationToken);

        if (trainingExerciseScores.Count == 0)
        {
            _logger.LogInformation(
                "No exercise scores found for Training {TrainingId} - skipping main record updates",
                command.TrainingId);
            return;
        }

        var exerciseScoreIds = trainingExerciseScores.Select(te => te.ExerciseScoreId).ToList();
        var exerciseScores = await _exerciseScoreRepository.GetByIdsAsync(exerciseScoreIds, cancellationToken);
        var exerciseScoreDict = exerciseScores.ToDictionary(es => es.Id);

        // Extract best score per exercise from fetched data
        var bestScoresByExercise = new Dictionary<Domain.ValueObjects.Id<Domain.Entities.Exercise>, Weight>();

        foreach (var trainingExercise in trainingExerciseScores)
        {
            if (!exerciseScoreDict.TryGetValue(trainingExercise.ExerciseScoreId, out var score))
            {
                continue;
            }

            // Skip invalid entries
            if (score.Weight.Unit == WeightUnits.Unknown)
            {
                continue;
            }

            // Skip exercises with partial reps — only full reps qualify for records
            if (score.Reps < 1)
            {
                continue;
            }

            // Track best weight per exercise within this training session
            if (!bestScoresByExercise.TryGetValue(score.ExerciseId, out var currentBest))
            {
                bestScoresByExercise[score.ExerciseId] = score.Weight;
                continue;
            }

            if (CompareWeights(score.Weight, currentBest) > 0)
            {
                bestScoresByExercise[score.ExerciseId] = score.Weight;
            }
        }

        if (bestScoresByExercise.Count == 0)
        {
            _logger.LogInformation(
                "No valid exercises with weights found for Training {TrainingId} - skipping main record updates",
                command.TrainingId);
            return;
        }

        // Fetch existing personal records for these exercises
        var existingRecords = await _mainRecordRepository.GetBestByUserGroupedByExerciseAndUnitAsync(
            command.UserId,
            bestScoresByExercise.Keys.ToList(),
            cancellationToken);

        var existingRecordsByExercise = existingRecords
            .GroupBy(r => r.ExerciseId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var recordDate = training.CreatedAt;
        var newRecordsCount = 0;

        foreach (var (exerciseId, bestScore) in bestScoresByExercise)
        {
            existingRecordsByExercise.TryGetValue(exerciseId, out var records);
            records ??= new List<MainRecordEntity>();

            var comparableRecords = records
                .Where(r => r.Weight.Unit != WeightUnits.Unknown)
                .ToList();

            // No existing record for this exercise - create first record
            if (comparableRecords.Count == 0)
            {
                await _mainRecordRepository.AddAsync(new MainRecordEntity
                {
                    Id = Domain.ValueObjects.Id<MainRecordEntity>.New(),
                    UserId = (Domain.ValueObjects.Id<Domain.Entities.User>)command.UserId,
                    ExerciseId = (Domain.ValueObjects.Id<Domain.Entities.Exercise>)exerciseId,
                    Weight = bestScore,
                    Date = recordDate
                }, cancellationToken);
                newRecordsCount++;
                continue;
            }

            // Find current best from existing records
            var currentBestRecord = comparableRecords[0];
            foreach (var candidateRecord in comparableRecords.Skip(1))
            {
                if (CompareWeights(candidateRecord.Weight, currentBestRecord.Weight) > 0)
                {
                    currentBestRecord = candidateRecord;
                }
            }

            // Compare training best to existing best - create new record if improved
            var comparison = CompareWeights(bestScore, currentBestRecord.Weight);
            if (comparison > 0)
            {
                await _mainRecordRepository.AddAsync(new MainRecordEntity
                {
                    Id = Domain.ValueObjects.Id<MainRecordEntity>.New(),
                    UserId = (Domain.ValueObjects.Id<Domain.Entities.User>)command.UserId,
                    ExerciseId = (Domain.ValueObjects.Id<Domain.Entities.Exercise>)exerciseId,
                    Weight = bestScore,
                    Date = recordDate
                }, cancellationToken);
                newRecordsCount++;
            }
        }

        if (newRecordsCount > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Main records synchronized for Training {TrainingId}: {NewRecordsCount} new personal records created",
            command.TrainingId,
            newRecordsCount);
    }

    /// <summary>
    /// Compares two weights accounting for unit differences.
    /// Returns: positive if weight1 > weight2, negative if weight1 &lt; weight2, zero if equal.
    /// </summary>
    private int CompareWeights(Weight weight1, Weight weight2)
    {
        // Normalize both weights to kilograms for comparison
        var normalizedWeight1 = weight1.Unit == WeightUnits.Kilograms
            ? weight1.Value
            : _weightUnitConverter.Convert(weight1.Value, weight1.Unit, WeightUnits.Kilograms);

        var normalizedWeight2 = weight2.Unit == WeightUnits.Kilograms
            ? weight2.Value
            : _weightUnitConverter.Convert(weight2.Value, weight2.Unit, WeightUnits.Kilograms);

        return normalizedWeight1.CompareTo(normalizedWeight2);
    }
}
