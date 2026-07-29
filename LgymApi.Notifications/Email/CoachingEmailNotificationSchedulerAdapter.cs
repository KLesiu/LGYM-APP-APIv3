using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;

namespace LgymApi.Notifications;

internal sealed class CoachingEmailNotificationSchedulerAdapter(
    IEmailNotificationsFeature feature,
    IEmailScheduler<InvitationEmailPayload> createdScheduler,
    IEmailScheduler<InvitationAcceptedEmailPayload> acceptedScheduler,
    IEmailScheduler<InvitationRevokedEmailPayload> revokedScheduler) :
    ICoachingEmailNotificationFeature,
    ICoachingEmailNotificationScheduler
{
    public bool Enabled => feature.Enabled;

    public Task ScheduleAsync(CoachingEmailSchedulingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind switch
        {
            CoachingEmailSchedulingKind.InvitationCreated => createdScheduler.ScheduleAsync(new InvitationEmailPayload
            {
                InvitationId = request.InvitationId,
                InvitationCode = request.InvitationCode ?? throw new InvalidOperationException("Invitation-created email scheduling requires an invitation code."),
                ExpiresAt = request.ExpiresAt ?? throw new InvalidOperationException("Invitation-created email scheduling requires an expiration."),
                TrainerName = request.TrainerName,
                RecipientEmail = request.RecipientEmail,
                CultureName = request.CultureName,
                PreferredTimeZone = request.PreferredTimeZone
            }, cancellationToken),
            CoachingEmailSchedulingKind.InvitationAccepted => acceptedScheduler.ScheduleAsync(new InvitationAcceptedEmailPayload
            {
                InvitationId = request.InvitationId,
                TrainerName = request.TrainerName,
                TraineeName = request.TraineeName ?? throw new InvalidOperationException("Invitation-accepted email scheduling requires a trainee name."),
                RecipientEmail = request.RecipientEmail,
                CultureName = request.CultureName,
                PreferredTimeZone = request.PreferredTimeZone
            }, cancellationToken),
            CoachingEmailSchedulingKind.InvitationRevoked => revokedScheduler.ScheduleAsync(new InvitationRevokedEmailPayload
            {
                InvitationId = request.InvitationId,
                TrainerName = request.TrainerName,
                RecipientEmail = request.RecipientEmail,
                CultureName = request.CultureName,
                PreferredTimeZone = request.PreferredTimeZone
            }, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Kind, "Unsupported Coaching email scheduling kind.")
        };
    }
}
