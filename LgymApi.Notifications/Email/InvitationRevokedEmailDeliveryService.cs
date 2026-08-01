using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Email;

internal sealed class InvitationRevokedEmailDeliveryService(ICoachingNotificationIntentService notificationIntentService, ICoachingEmailNotificationScheduler emailScheduler, ILogger<InvitationRevokedEmailDeliveryService> logger) : IInvitationRevokedEmailDeliveryPort
{
    public async Task DeliverAsync(InvitationRevokedEmailDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<TrainerInvitation>.TryParse(request.InvitationId, out var invitationId) || !Id<User>.TryParse(request.TrainerId, out var trainerId)) return;
        var trainer = new AccountReadModel(trainerId, request.TrainerName, request.TrainerEmail, null, request.TrainerCultureName, request.TrainerTimeZone);
        var result = await notificationIntentService.SubmitAsync(new InvitationRevokedCoachingNotificationIntent(CoachingNotificationLegacyChannel.Email, invitationId, trainerId, request.InviteeEmail, trainer), cancellationToken);
        if (result.EmailSchedulingRequest is null) return;
        await emailScheduler.ScheduleAsync(result.EmailSchedulingRequest, cancellationToken);
        logger.LogInformation("InvitationRevoked email scheduled for Invitation {InvitationId} to {Email}", invitationId, result.EmailSchedulingRequest.RecipientEmail);
    }
}
