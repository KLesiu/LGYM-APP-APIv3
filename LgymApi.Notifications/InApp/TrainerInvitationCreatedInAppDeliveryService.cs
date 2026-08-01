using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Contracts.InApp
{
    public sealed record TrainerInvitationCreatedInAppDeliveryRequest(
        string InvitationId,
        string TrainerId,
        string TraineeId,
        string InviteeEmail,
        string InvitationCode,
        DateTimeOffset ExpiresAt,
        string TrainerName,
        string TrainerEmail,
        string TrainerCultureName,
        string TrainerTimeZone);

    public interface ITrainerInvitationCreatedInAppDeliveryPort
    {
        Task DeliverAsync(TrainerInvitationCreatedInAppDeliveryRequest request, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Notifications.InApp
{
    internal sealed class TrainerInvitationCreatedInAppDeliveryService(
        ICoachingNotificationIntentService notificationIntentService,
        ILogger<TrainerInvitationCreatedInAppDeliveryService> logger) : ITrainerInvitationCreatedInAppDeliveryPort
    {
        public async Task DeliverAsync(TrainerInvitationCreatedInAppDeliveryRequest request, CancellationToken cancellationToken = default)
        {
            if (!Id<TrainerInvitation>.TryParse(request.InvitationId, out var invitationId)
                || !Id<User>.TryParse(request.TrainerId, out var trainerId)
                || !Id<User>.TryParse(request.TraineeId, out var traineeId))
            {
                return;
            }

            var trainer = string.IsNullOrEmpty(request.TrainerName)
                ? null
                : new AccountReadModel(trainerId, request.TrainerName, request.TrainerEmail, null, request.TrainerCultureName, request.TrainerTimeZone);
            var result = await notificationIntentService.SubmitAsync(
                new InvitationCreatedCoachingNotificationIntent(
                    CoachingNotificationLegacyChannel.InApp,
                    invitationId,
                    trainerId,
                    traineeId,
                    request.InviteeEmail,
                    request.InvitationCode,
                    request.ExpiresAt,
                    trainer,
                    null),
                cancellationToken);
            if (result.InAppError is not null)
            {
                logger.LogError(
                    "Failed to create invitation-sent notification for trainee {TraineeId}: {Error}",
                    traineeId,
                    result.InAppError);
            }
        }
    }
}
