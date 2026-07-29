using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Contracts.InApp
{
    public sealed record TrainerInvitationAcceptedInAppDeliveryRequest(string InvitationId, string TrainerId, string TraineeId);

    public interface ITrainerInvitationAcceptedInAppDeliveryPort
    {
        Task DeliverAsync(TrainerInvitationAcceptedInAppDeliveryRequest request, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Notifications.InApp
{

internal sealed class TrainerInvitationAcceptedInAppDeliveryService(
    ICoachingNotificationIntentService notificationIntentService,
    ILogger<TrainerInvitationAcceptedInAppDeliveryService> logger) : ITrainerInvitationAcceptedInAppDeliveryPort
{
    public async Task DeliverAsync(TrainerInvitationAcceptedInAppDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<TrainerInvitation>.TryParse(request.InvitationId, out var invitationId)
            || !Id<User>.TryParse(request.TrainerId, out var trainerId)
            || !Id<User>.TryParse(request.TraineeId, out var traineeId)) return;

        var result = await notificationIntentService.SubmitAsync(new InvitationAcceptedCoachingNotificationIntent(
            CoachingNotificationLegacyChannel.InApp, invitationId, trainerId, traineeId, null, null), cancellationToken);
        if (result.InAppError is not null)
            logger.LogError("Failed to create invitation-accepted notification for trainer {TrainerId}: {Error}", trainerId, result.InAppError);
    }
}
}
