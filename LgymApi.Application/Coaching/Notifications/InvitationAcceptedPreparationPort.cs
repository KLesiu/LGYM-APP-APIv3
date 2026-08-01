using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Platform.Contracts.Serialization;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Coaching.Contracts.Notifications
{
    public sealed record InvitationAcceptedEmailPreparation(
        string InvitationId,
        string TrainerId,
        string TraineeId,
        string TrainerEmail,
        string TrainerCultureName,
        string TrainerTimeZone,
        string TrainerName,
        string TraineeName);

    public interface IInvitationAcceptedEmailPreparationPort
    {
        Task<InvitationAcceptedEmailPreparation?> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Coaching.Notifications
{
    internal sealed class InvitationAcceptedEmailPreparationPort(
        ICoachingNotificationReadService notificationReadService,
        IAccountReadService accountReadService,
        ILogger<InvitationAcceptedEmailPreparationPort> logger) : IInvitationAcceptedEmailPreparationPort
    {
        public async Task<InvitationAcceptedEmailPreparation?> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            var command = JsonSerializer.Deserialize<InvitationAcceptedCommand>(payloadJson, SharedSerializationOptions.Current)
                ?? throw new InvalidOperationException("Invitation accepted action payload is invalid.");
            var invitation = await notificationReadService.GetInvitationAsync(command.InvitationId, cancellationToken);
            if (invitation is null)
            {
                logger.LogWarning("Invitation not found for InvitationAccepted {InvitationId}", command.InvitationId);
                return null;
            }

            var trainer = await accountReadService.GetByIdAsync(invitation.TrainerId, cancellationToken);
            if (trainer is null)
            {
                logger.LogWarning("Trainer user not found for InvitationAccepted {InvitationId}, TrainerId {TrainerId}", command.InvitationId, invitation.TrainerId);
                return null;
            }

            if (!invitation.TraineeId.HasValue)
            {
                logger.LogWarning("InvitationAccepted email skipped for Invitation {InvitationId} - TraineeId is null", command.InvitationId);
                return null;
            }

            var trainee = await accountReadService.GetByIdAsync(invitation.TraineeId.Value, cancellationToken);
            if (trainee is null)
            {
                logger.LogWarning("Trainee user not found for InvitationAccepted {InvitationId}, TraineeId {TraineeId}", command.InvitationId, invitation.TraineeId);
                return null;
            }

            return new InvitationAcceptedEmailPreparation(
                invitation.InvitationId.ToString(),
                invitation.TrainerId.ToString(),
                invitation.TraineeId.Value.ToString(),
                trainer.Email,
                trainer.PreferredLanguage,
                trainer.PreferredTimeZone,
                trainer.Name,
                trainee.Name);
        }
    }
}
