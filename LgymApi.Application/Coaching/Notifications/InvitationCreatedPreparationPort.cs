using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Platform.Contracts.Serialization;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Coaching.Contracts.Notifications
{
    public sealed record InvitationCreatedEmailPreparation(
        string InvitationId,
        string TrainerId,
        string? TraineeId,
        string InviteeEmail,
        string InvitationCode,
        DateTimeOffset ExpiresAt,
        string TrainerName,
        string TrainerEmail,
        string TrainerCultureName,
        string TrainerTimeZone,
        string? TraineeName,
        string? TraineeEmail,
        string? TraineeTimeZone);

    public interface IInvitationCreatedEmailPreparationPort
    {
        Task<InvitationCreatedEmailPreparation?> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Coaching.Notifications
{
    internal sealed class InvitationCreatedEmailPreparationPort(
        ICoachingNotificationReadService notificationReadService,
        IAccountReadService accountReadService,
        ILogger<InvitationCreatedEmailPreparationPort> logger) : IInvitationCreatedEmailPreparationPort
    {
        public async Task<InvitationCreatedEmailPreparation?> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            var command = JsonSerializer.Deserialize<InvitationCreatedCommand>(payloadJson, SharedSerializationOptions.Current)
                ?? throw new InvalidOperationException("Invitation created action payload is invalid.");
            var invitation = await notificationReadService.GetInvitationAsync(command.InvitationId, cancellationToken);
            if (invitation is null)
            {
                logger.LogWarning("Invitation not found for Invitation {InvitationId}", command.InvitationId);
                return null;
            }

            var trainer = await accountReadService.GetByIdAsync(invitation.TrainerId, cancellationToken);
            if (trainer is null)
            {
                logger.LogWarning("Trainer user not found for Invitation {InvitationId}, TrainerId {TrainerId}", command.InvitationId, invitation.TrainerId);
                return null;
            }

            var trainee = invitation.TraineeId.HasValue
                ? await accountReadService.GetByIdAsync(invitation.TraineeId.Value, cancellationToken)
                : null;
            if (invitation.TraineeId.HasValue && trainee is null)
            {
                logger.LogWarning("Trainee user not found for Invitation {InvitationId}, TraineeId {TraineeId}", command.InvitationId, invitation.TraineeId);
                return null;
            }

            return new InvitationCreatedEmailPreparation(
                invitation.InvitationId.ToString(),
                invitation.TrainerId.ToString(),
                invitation.TraineeId?.ToString(),
                invitation.InviteeEmail,
                invitation.InvitationCode,
                invitation.ExpiresAt,
                trainer.Name,
                trainer.Email,
                trainer.PreferredLanguage,
                trainer.PreferredTimeZone,
                trainee?.Name,
                trainee?.Email,
                trainee?.PreferredTimeZone);
        }
    }
}
