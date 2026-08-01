using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Platform.Contracts.Serialization;

namespace LgymApi.Application.Coaching.Contracts.Notifications
{
    public sealed record TrainerInvitationCreatedInAppPreparation(
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

    public interface ITrainerInvitationCreatedInAppPreparationPort
    {
        Task<TrainerInvitationCreatedInAppPreparation?> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Coaching.Notifications
{
    internal sealed class TrainerInvitationCreatedInAppPreparationPort(
        ICoachingNotificationReadService notificationReadService,
        IAccountReadService accountReadService) : ITrainerInvitationCreatedInAppPreparationPort
    {
        public async Task<TrainerInvitationCreatedInAppPreparation?> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            var command = JsonSerializer.Deserialize<TrainerInvitationCreatedInAppNotificationCommand>(payloadJson, SharedSerializationOptions.Current)
                ?? throw new InvalidOperationException("Trainer invitation created action payload is invalid.");
            var invitation = await notificationReadService.GetInvitationAsync(command.InvitationId, cancellationToken);
            if (invitation is null)
            {
                return null;
            }

            var trainer = await accountReadService.GetByIdAsync(command.TrainerId, cancellationToken);
            return new TrainerInvitationCreatedInAppPreparation(
                command.InvitationId.ToString(),
                command.TrainerId.ToString(),
                command.TraineeId.ToString(),
                invitation.InviteeEmail,
                invitation.InvitationCode,
                invitation.ExpiresAt,
                trainer?.Name ?? string.Empty,
                trainer?.Email ?? string.Empty,
                trainer?.PreferredLanguage ?? string.Empty,
                trainer?.PreferredTimeZone ?? string.Empty);
        }
    }
}
