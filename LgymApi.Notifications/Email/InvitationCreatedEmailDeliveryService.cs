using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Email;

internal sealed class InvitationCreatedEmailDeliveryService(
    ICoachingNotificationIntentService notificationIntentService,
    IEmailScheduler<InvitationEmailPayload> emailScheduler,
    ILogger<InvitationCreatedEmailDeliveryService> logger) : IInvitationCreatedEmailDeliveryPort
{
    public async Task DeliverAsync(InvitationCreatedEmailDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<TrainerInvitation>.TryParse(request.InvitationId, out var invitationId)
            || !Id<User>.TryParse(request.TrainerId, out var trainerId))
        {
            return;
        }

        Id<User>? traineeId = null;
        if (request.TraineeId is not null)
        {
            if (!Id<User>.TryParse(request.TraineeId, out var parsedTraineeId))
            {
                return;
            }

            traineeId = parsedTraineeId;
        }

        var trainer = new AccountReadModel(trainerId, request.TrainerName, request.TrainerEmail, null, request.TrainerCultureName, request.TrainerTimeZone);
        var trainee = request.TraineeId is null
            ? null
            : new AccountReadModel(traineeId!.Value, request.TraineeName!, request.TraineeEmail!, null, string.Empty, request.TraineeTimeZone!);
        var result = await notificationIntentService.SubmitAsync(
            new InvitationCreatedCoachingNotificationIntent(
                CoachingNotificationLegacyChannel.Email,
                invitationId,
                trainerId,
                traineeId,
                request.InviteeEmail,
                request.InvitationCode,
                request.ExpiresAt,
                trainer,
                trainee),
            cancellationToken);
        var schedulingRequest = result.EmailSchedulingRequest;
        if (schedulingRequest is null)
        {
            return;
        }

        await emailScheduler.ScheduleAsync(new InvitationEmailPayload
        {
            InvitationId = invitationId,
            InvitationCode = schedulingRequest.InvitationCode ?? throw new InvalidOperationException("Invitation-created email scheduling requires an invitation code."),
            ExpiresAt = schedulingRequest.ExpiresAt ?? throw new InvalidOperationException("Invitation-created email scheduling requires an expiration."),
            TrainerName = schedulingRequest.TrainerName,
            RecipientEmail = schedulingRequest.RecipientEmail,
            CultureName = schedulingRequest.CultureName,
            PreferredTimeZone = schedulingRequest.PreferredTimeZone
        }, cancellationToken);
        logger.LogInformation("Invitation email scheduled for Invitation {InvitationId} to {Email}", invitationId, schedulingRequest.RecipientEmail);
    }
}
