using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Contracts.InApp
{
    public sealed record TraineeNoteUpdatedInAppDeliveryRequest(
        string TraineeNoteId,
        string TraineeId,
        string TrainerId,
        string? NoteTitle,
        DateTimeOffset TriggeredAt,
        string? TrainerName,
        string? TrainerEmail,
        string? TrainerCultureName,
        string? TrainerTimeZone,
        string? TraineeName,
        string? TraineeEmail,
        string? TraineeCultureName,
        string? TraineeTimeZone);

    public interface ITraineeNoteUpdatedInAppDeliveryPort
    {
        Task DeliverAsync(TraineeNoteUpdatedInAppDeliveryRequest request, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Notifications.InApp
{
    internal sealed class TraineeNoteUpdatedInAppDeliveryService(
        ICoachingNotificationIntentService notificationIntentService,
        ILogger<TraineeNoteUpdatedInAppDeliveryService> logger) : ITraineeNoteUpdatedInAppDeliveryPort
    {
        public async Task DeliverAsync(TraineeNoteUpdatedInAppDeliveryRequest request, CancellationToken cancellationToken = default)
        {
            if (!Id<TraineeNote>.TryParse(request.TraineeNoteId, out var traineeNoteId)
                || !Id<User>.TryParse(request.TraineeId, out var traineeId)
                || !Id<User>.TryParse(request.TrainerId, out var trainerId))
            {
                return;
            }

            var trainer = CreateAccount(trainerId, request.TrainerName, request.TrainerEmail, request.TrainerCultureName, request.TrainerTimeZone);
            var trainee = CreateAccount(traineeId, request.TraineeName, request.TraineeEmail, request.TraineeCultureName, request.TraineeTimeZone);
            var result = await notificationIntentService.SubmitAsync(
                new TraineeNoteUpdatedCoachingNotificationIntent(
                    CoachingNotificationLegacyChannel.InApp,
                    traineeNoteId,
                    traineeId,
                    trainerId,
                    request.NoteTitle,
                    request.TriggeredAt,
                    trainer,
                    trainee),
                cancellationToken);
            if (result.InAppError is not null)
            {
                logger.LogError("Failed to create trainee note notification for trainee {TraineeId}: {Error}", traineeId, result.InAppError);
            }
        }

        private static AccountReadModel? CreateAccount(Id<User> id, string? name, string? email, string? cultureName, string? timeZone)
        {
            return name is null
                ? null
                : new AccountReadModel(id, name, email!, null, cultureName!, timeZone!);
        }
    }
}
