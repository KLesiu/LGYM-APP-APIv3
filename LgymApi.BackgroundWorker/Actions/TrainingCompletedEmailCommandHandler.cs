using LgymApi.Application.Repositories;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Common;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Application.Options;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.BackgroundWorker.Actions;

/// <summary>
/// Background action handler that schedules training completed email notifications.
/// Triggered when a training session is completed.
/// </summary>
public sealed partial class TrainingCompletedEmailCommandHandler : global::LgymApi.BackgroundWorker.Actions.Contracts.IBackgroundAction<TrainingCompletedCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ITrainingRepository _trainingRepository;
    private readonly ITrainingExerciseScoreRepository _trainingExerciseScoreRepository;
    private readonly IExerciseScoreRepository _exerciseScoreRepository;
    private readonly IEmailNotificationSubscriptionRepository _emailNotificationSubscriptionRepository;
    private readonly IEmailScheduler<TrainingCompletedEmailPayload> _emailScheduler;
    private readonly ILogger<TrainingCompletedEmailCommandHandler> _logger;
    private readonly AppDefaultsOptions _appDefaultsOptions;

    public TrainingCompletedEmailCommandHandler(
        IUserRepository userRepository,
        ITrainingRepository trainingRepository,
        ITrainingExerciseScoreRepository trainingExerciseScoreRepository,
        IExerciseScoreRepository exerciseScoreRepository,
        IEmailNotificationSubscriptionRepository emailNotificationSubscriptionRepository,
        IEmailScheduler<TrainingCompletedEmailPayload> emailScheduler,
        ILogger<TrainingCompletedEmailCommandHandler> logger,
        AppDefaultsOptions appDefaultsOptions)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _trainingRepository = trainingRepository ?? throw new ArgumentNullException(nameof(trainingRepository));
        _trainingExerciseScoreRepository = trainingExerciseScoreRepository ?? throw new ArgumentNullException(nameof(trainingExerciseScoreRepository));
        _exerciseScoreRepository = exerciseScoreRepository ?? throw new ArgumentNullException(nameof(exerciseScoreRepository));
        _emailNotificationSubscriptionRepository = emailNotificationSubscriptionRepository ?? throw new ArgumentNullException(nameof(emailNotificationSubscriptionRepository));
        _emailScheduler = emailScheduler ?? throw new ArgumentNullException(nameof(emailScheduler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _appDefaultsOptions = appDefaultsOptions ?? throw new ArgumentNullException(nameof(appDefaultsOptions));
    }

    public async Task ExecuteAsync(TrainingCompletedCommand command, CancellationToken cancellationToken = default)
    {
        // Fetch user by ID
        var user = await _userRepository.FindByIdAsync((Id<LgymApi.Domain.Entities.User>)command.UserId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning(
                "Training completed email skipped for Training {TrainingId} - user {UserId} not found",
                command.TrainingId,
                command.UserId);
            return;
        }

        // Skip scheduling if recipient email is empty (graceful degradation)
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning(
                "Training completed email skipped for Training {TrainingId} - no recipient email for user {UserId}",
                command.TrainingId,
                command.UserId);
            return;
        }

        var isSubscribed = await _emailNotificationSubscriptionRepository.IsSubscribedAsync(
            command.UserId,
            Domain.Notifications.EmailNotificationTypes.TrainingCompleted.Value,
            cancellationToken);

        if (!isSubscribed)
        {
            _logger.LogInformation(
                "Training completed email skipped for Training {TrainingId} - subscription is disabled for user {UserId}",
                command.TrainingId,
                command.UserId);
            return;
        }

        // Fetch training exercises
        var trainingExercises = await _trainingExerciseScoreRepository.GetByTrainingIdsAsync(
            new List<Domain.ValueObjects.Id<Domain.Entities.Training>> { (Domain.ValueObjects.Id<Domain.Entities.Training>)command.TrainingId },
            cancellationToken);

        var exerciseScoreIds = trainingExercises.Select(te => te.ExerciseScoreId).ToList();
        var exerciseScores = exerciseScoreIds.Any()
            ? await _exerciseScoreRepository.GetByIdsAsync(exerciseScoreIds, cancellationToken)
            : new List<Domain.Entities.ExerciseScore>();

        // Build exercise summaries
        var exercises = trainingExercises
            .Select(te =>
            {
                var score = exerciseScores.FirstOrDefault(es => es.Id == te.ExerciseScoreId);
                return new TrainingExerciseSummary
                {
                    ExerciseId = score?.ExerciseId ?? Id<Domain.Entities.Exercise>.Empty,
                    ExerciseName = score?.Exercise?.Name ?? string.Empty,
                    Series = score?.Series ?? 0,
                    Reps = score?.Reps ?? 0,
                    Weight = score?.Weight.Value ?? 0,
                    Unit = score?.Weight.Unit ?? Domain.Enums.WeightUnits.Kilograms
                };
            })
            .ToList();

        // Fetch training to get plan day name and training date
        var training = await _trainingRepository.GetByIdAsync((Domain.ValueObjects.Id<Domain.Entities.Training>)command.TrainingId, cancellationToken);
        if (training == null)
        {
            _logger.LogWarning(
                "Training completed email skipped for Training {TrainingId} - training not found",
                command.TrainingId);
            return;
        }

        var planDayName = training.PlanDay?.Name ?? string.Empty;
        var trainingDate = training.CreatedAt;

        // Map command to email payload
        var emailPayload = new TrainingCompletedEmailPayload
        {
            UserId = command.UserId,
            TrainingId = command.TrainingId,
            RecipientEmail = user.Email,
            CultureName = string.IsNullOrWhiteSpace(user.PreferredLanguage) ? _appDefaultsOptions.PreferredLanguage : user.PreferredLanguage,
            PreferredTimeZone = string.IsNullOrWhiteSpace(user.PreferredTimeZone) ? _appDefaultsOptions.PreferredTimeZone : user.PreferredTimeZone,
            PlanDayName = planDayName,
            TrainingDate = trainingDate,
            Exercises = exercises
        };

        // Schedule email via typed email scheduler
        await _emailScheduler.ScheduleAsync(emailPayload, cancellationToken);

        _logger.LogInformation(
            "Training completed email scheduled for Training {TrainingId} to {Email}",
            command.TrainingId,
            user.Email);
    }

}
