using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Platform.Contracts.Serialization;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Coaching.Contracts.Notifications
{
    public sealed record InvitationRevokedEmailPreparation(string InvitationId, string TrainerId, string InviteeEmail, string TrainerEmail, string TrainerCultureName, string TrainerTimeZone, string TrainerName);

    public interface IInvitationRevokedEmailPreparationPort
    {
        Task<InvitationRevokedEmailPreparation?> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Coaching.Notifications
{
    internal sealed class InvitationRevokedEmailPreparationPort(ICoachingNotificationReadService notificationReadService, IAccountReadService accountReadService, ILogger<InvitationRevokedEmailPreparationPort> logger) : IInvitationRevokedEmailPreparationPort
    {
        public async Task<InvitationRevokedEmailPreparation?> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            var command = JsonSerializer.Deserialize<InvitationRevokedCommand>(payloadJson, SharedSerializationOptions.Current) ?? throw new InvalidOperationException("Invitation revoked action payload is invalid.");
            var invitation = await notificationReadService.GetInvitationAsync(command.InvitationId, cancellationToken);
            if (invitation is null)
            {
                logger.LogWarning("Invitation not found for InvitationRevoked {InvitationId}", command.InvitationId);
                return null;
            }

            var trainer = await accountReadService.GetByIdAsync(invitation.TrainerId, cancellationToken);
            if (trainer is null)
            {
                logger.LogWarning("Trainer user not found for InvitationRevoked {InvitationId}, TrainerId {TrainerId}", command.InvitationId, invitation.TrainerId);
                return null;
            }

            return new InvitationRevokedEmailPreparation(invitation.InvitationId.ToString(), invitation.TrainerId.ToString(), invitation.InviteeEmail, trainer.Email, trainer.PreferredLanguage, trainer.PreferredTimeZone, trainer.Name);
        }
    }
}
