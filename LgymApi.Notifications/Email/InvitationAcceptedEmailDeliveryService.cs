using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Email;

internal sealed class InvitationAcceptedEmailDeliveryService(
    ICoachingNotificationIntentService notificationIntentService,
    ICoachingEmailNotificationScheduler emailScheduler,
    ILogger<InvitationAcceptedEmailDeliveryService> logger) : IInvitationAcceptedEmailDeliveryPort
{
    public async Task DeliverAsync(InvitationAcceptedEmailDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<TrainerInvitation>.TryParse(request.InvitationId, out var invitationId)
            || !Id<User>.TryParse(request.TrainerId, out var trainerId)
            || !Id<User>.TryParse(request.TraineeId, out var traineeId))
        {
            return;
        }

        var trainer = new AccountReadModel(trainerId, request.TrainerName, request.TrainerEmail, null, request.TrainerCultureName, request.TrainerTimeZone);
        var trainee = new AccountReadModel(traineeId, request.TraineeName, string.Empty, null, string.Empty, string.Empty);
        var result = await notificationIntentService.SubmitAsync(
            new InvitationAcceptedCoachingNotificationIntent(CoachingNotificationLegacyChannel.Email, invitationId, trainerId, traineeId, trainer, trainee),
            cancellationToken);
        if (result.EmailSchedulingRequest is null)
        {
            return;
        }

        await emailScheduler.ScheduleAsync(result.EmailSchedulingRequest, cancellationToken);
        logger.LogInformation("InvitationAccepted email scheduled for Invitation {InvitationId} to {Email}", invitationId, result.EmailSchedulingRequest.RecipientEmail);
    }
}
