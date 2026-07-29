using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.Serialization;

namespace LgymApi.Application.Coaching.Contracts.Notifications
{
    public sealed record TrainerInvitationAcceptedInAppPreparation(string InvitationId, string TrainerId, string TraineeId);

    public interface ITrainerInvitationAcceptedInAppPreparationPort
    {
        Task<TrainerInvitationAcceptedInAppPreparation> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Coaching.Notifications
{
using LgymApi.Application.Coaching.Contracts.Notifications;

internal sealed class TrainerInvitationAcceptedInAppPreparationPort : ITrainerInvitationAcceptedInAppPreparationPort
{
    public Task<TrainerInvitationAcceptedInAppPreparation> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        var command = JsonSerializer.Deserialize<TrainerInvitationAcceptedInAppNotificationCommand>(payloadJson, SharedSerializationOptions.Current)
            ?? throw new InvalidOperationException("Trainer invitation accepted in-app notification payload is invalid.");
        return Task.FromResult(new TrainerInvitationAcceptedInAppPreparation(command.InvitationId.ToString(), command.TrainerId.ToString(), command.TraineeId.ToString()));
    }
}
}
