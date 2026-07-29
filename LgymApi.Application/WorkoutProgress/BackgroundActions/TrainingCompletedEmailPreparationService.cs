using LgymApi.Application.Repositories;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.Application.Options;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace LgymApi.Application.WorkoutProgress.BackgroundActions;

/// <summary>
/// Background action handler that schedules training completed email notifications.
/// Triggered when a training session is completed.
/// </summary>
internal sealed class TrainingCompletedEmailPreparationService : Contracts.BackgroundActions.ITrainingCompletedEmailPreparationPort
{
    private readonly IAccountReadService _accountReadService;
    private readonly TrainingCompletedExercisePreparationData _exerciseData;
    private readonly ILogger<TrainingCompletedEmailPreparationService> _logger;
    private readonly AppDefaultsOptions _appDefaultsOptions;

    public TrainingCompletedEmailPreparationService(
        IAccountReadService accountReadService,
        TrainingCompletedExercisePreparationData exerciseData,
        ILogger<TrainingCompletedEmailPreparationService> logger,
        AppDefaultsOptions appDefaultsOptions)
    {
        _accountReadService = accountReadService ?? throw new ArgumentNullException(nameof(accountReadService));
        _exerciseData = exerciseData ?? throw new ArgumentNullException(nameof(exerciseData));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _appDefaultsOptions = appDefaultsOptions ?? throw new ArgumentNullException(nameof(appDefaultsOptions));
    }

    public async Task<Contracts.BackgroundActions.TrainingCompletedEmailPreparation?> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        var command = JsonSerializer.Deserialize<TrainingCompletedCommand>(payloadJson, SharedSerializationOptions.Current)
            ?? throw new InvalidOperationException("Training completed action payload is invalid.");
        // Fetch user by ID
        var user = await _accountReadService.GetByIdAsync((Id<LgymApi.Domain.Entities.User>)command.UserId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning(
                "Training completed email skipped for Training {TrainingId} - user {UserId} not found",
                command.TrainingId,
                command.UserId);
            return null;
        }

        // Skip scheduling if recipient email is empty (graceful degradation)
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "Training completed email skipped for Training {TrainingId} - no recipient email for user {UserId}",
                command.TrainingId,
                command.UserId);
            return null;
        }

        // Fetch training exercises
        var trainingExercises = await _exerciseData.TrainingExerciseScores.GetByTrainingIdsAsync(
            new List<Domain.ValueObjects.Id<Domain.Entities.Training>> { (Domain.ValueObjects.Id<Domain.Entities.Training>)command.TrainingId },
            cancellationToken);

        var exerciseScoreIds = trainingExercises.Select(te => te.ExerciseScoreId).ToList();
        var exerciseScores = exerciseScoreIds.Any()
            ? await _exerciseData.ExerciseScores.GetByIdsAsync(exerciseScoreIds, cancellationToken)
            : new List<Domain.Entities.ExerciseScore>();

        // Build exercise summaries
        var exercises = trainingExercises
            .Select(te =>
            {
                var score = exerciseScores.FirstOrDefault(es => es.Id == te.ExerciseScoreId);
                return new Contracts.BackgroundActions.TrainingCompletedExercisePreparation(
                    (score?.ExerciseId ?? Id<Domain.Entities.Exercise>.Empty).ToString(),
                    score?.Exercise?.Name ?? string.Empty,
                    score?.Series ?? 0,
                    score?.Reps ?? 0,
                    score?.Weight.Value ?? 0,
                    (score?.Weight.Unit ?? Domain.Enums.WeightUnits.Kilograms).ToString());
            })
            .ToList();

        // Fetch training to get plan day name and training date
        var training = await _exerciseData.Trainings.GetByIdAsync((Domain.ValueObjects.Id<Domain.Entities.Training>)command.TrainingId, cancellationToken);
        if (training == null)
        {
            _logger.LogWarning(
                "Training completed email skipped for Training {TrainingId} - training not found",
                command.TrainingId);
            return null;
        }

        var planDayName = training.PlanDay?.Name ?? string.Empty;
        var trainingDate = training.CreatedAt;

        // Map command to email payload
        return new Contracts.BackgroundActions.TrainingCompletedEmailPreparation(
            command.UserId.ToString(),
            command.TrainingId.ToString(),
            user.Email,
            string.IsNullOrWhiteSpace(user.PreferredLanguage) ? _appDefaultsOptions.PreferredLanguage : user.PreferredLanguage,
            string.IsNullOrWhiteSpace(user.PreferredTimeZone) ? _appDefaultsOptions.PreferredTimeZone : user.PreferredTimeZone,
            planDayName,
            trainingDate,
            exercises);
    }

}

internal sealed class TrainingCompletedExercisePreparationData(
    ITrainingRepository trainings,
    ITrainingExerciseScoreRepository trainingExerciseScores,
    IExerciseScoreRepository exerciseScores)
{
    public ITrainingRepository Trainings { get; } = trainings;
    public ITrainingExerciseScoreRepository TrainingExerciseScores { get; } = trainingExerciseScores;
    public IExerciseScoreRepository ExerciseScores { get; } = exerciseScores;
}
