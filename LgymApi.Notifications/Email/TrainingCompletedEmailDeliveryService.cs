using LgymApi.Application.Repositories;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Email;

internal sealed class TrainingCompletedEmailDeliveryService(
    IEmailNotificationSubscriptionRepository subscriptions,
    IEmailSchedulingPort<TrainingCompletedEmailPayload> scheduler,
    ILogger<TrainingCompletedEmailDeliveryService> logger) : Contracts.Email.ITrainingCompletedEmailDeliveryPort
{
    public async Task DeliverAsync(TrainingCompletedEmailDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.TryParse(request.UserId, out var userId)
            || !LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training>.TryParse(request.TrainingId, out var trainingId)) return;
        var payload = new TrainingCompletedEmailPayload
        {
            UserId = userId,
            TrainingId = trainingId,
            RecipientEmail = request.RecipientEmail,
            CultureName = request.CultureName,
            PreferredTimeZone = request.PreferredTimeZone,
            PlanDayName = request.PlanDayName,
            TrainingDate = request.TrainingDate,
            Exercises = request.Exercises.Select(exercise => new TrainingExerciseSummary
            {
                ExerciseId = LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Exercise>.TryParse(exercise.ExerciseId, out var exerciseId) ? exerciseId : LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Exercise>.Empty,
                ExerciseName = exercise.ExerciseName,
                Series = exercise.Series,
                Reps = exercise.Reps,
                Weight = exercise.Weight,
                Unit = Enum.TryParse<LgymApi.Domain.Enums.WeightUnits>(exercise.Unit, out var unit) ? unit : LgymApi.Domain.Enums.WeightUnits.Kilograms
            }).ToList()
        };
        if (!await subscriptions.IsSubscribedAsync(payload.UserId, EmailNotificationTypes.TrainingCompleted.Value, cancellationToken))
        {
            logger.LogInformation("Training completed email skipped for Training {TrainingId} - subscription is disabled for user {UserId}", payload.TrainingId, payload.UserId);
            return;
        }

        await scheduler.ScheduleAsync(payload, cancellationToken);
        logger.LogInformation("Training completed email scheduled for Training {TrainingId} to {Email}", payload.TrainingId, payload.RecipientEmail);
    }
}
