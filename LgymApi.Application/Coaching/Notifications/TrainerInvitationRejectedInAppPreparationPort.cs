using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.Serialization;

namespace LgymApi.Application.Coaching.Contracts.Notifications
{
    public sealed record TrainerInvitationRejectedInAppPreparation(string InvitationId, string TrainerId, string TraineeId);

    public interface ITrainerInvitationRejectedInAppPreparationPort
    {
        Task<TrainerInvitationRejectedInAppPreparation> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Coaching.Notifications
{
    using LgymApi.Application.Coaching.Contracts.Notifications;

    internal sealed class TrainerInvitationRejectedInAppPreparationPort : ITrainerInvitationRejectedInAppPreparationPort
    {
        public Task<TrainerInvitationRejectedInAppPreparation> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            var command = JsonSerializer.Deserialize<TrainerInvitationRejectedInAppNotificationCommand>(payloadJson, SharedSerializationOptions.Current)
                ?? throw new InvalidOperationException("Trainer invitation rejected in-app notification payload is invalid.");
            return Task.FromResult(new TrainerInvitationRejectedInAppPreparation(command.InvitationId.ToString(), command.TrainerId.ToString(), command.TraineeId.ToString()));
        }
    }
}
