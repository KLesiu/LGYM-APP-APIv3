using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Contracts.InApp
{
    public sealed record TrainerInvitationRejectedInAppDeliveryRequest(string InvitationId, string TrainerId, string TraineeId);

    public interface ITrainerInvitationRejectedInAppDeliveryPort
    {
        Task DeliverAsync(TrainerInvitationRejectedInAppDeliveryRequest request, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Notifications.InApp
{
    internal sealed class TrainerInvitationRejectedInAppDeliveryService(
        ICoachingNotificationIntentService notificationIntentService,
        ILogger<TrainerInvitationRejectedInAppDeliveryService> logger) : ITrainerInvitationRejectedInAppDeliveryPort
    {
        public async Task DeliverAsync(TrainerInvitationRejectedInAppDeliveryRequest request, CancellationToken cancellationToken = default)
        {
            if (!Id<TrainerInvitation>.TryParse(request.InvitationId, out var invitationId)
                || !Id<User>.TryParse(request.TrainerId, out var trainerId)
                || !Id<User>.TryParse(request.TraineeId, out var traineeId)) return;

            var result = await notificationIntentService.SubmitAsync(new InvitationRejectedCoachingNotificationIntent(
                CoachingNotificationLegacyChannel.InApp, invitationId, trainerId, traineeId), cancellationToken);
            if (result.InAppError is not null)
                logger.LogError("Failed to create invitation-rejected notification for trainer {TrainerId}: {Error}", trainerId, result.InAppError);
        }
    }
}
