using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Actions.Contracts;

namespace LgymApi.BackgroundWorker.Actions;

public sealed class TrainerInvitationCreatedInAppNotificationCommandHandler(
    ITrainerInvitationCreatedInAppPreparationPort preparationPort,
    ITrainerInvitationCreatedInAppDeliveryPort deliveryPort) : IBackgroundAction<TrainerInvitationCreatedInAppNotificationCommand>
{
    public async Task ExecuteAsync(TrainerInvitationCreatedInAppNotificationCommand command, CancellationToken cancellationToken = default)
    {
        var preparation = await preparationPort.PrepareAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
        if (preparation is null)
        {
            return;
        }

        await deliveryPort.DeliverAsync(new TrainerInvitationCreatedInAppDeliveryRequest(
            preparation.InvitationId,
            preparation.TrainerId,
            preparation.TraineeId,
            preparation.InviteeEmail,
            preparation.InvitationCode,
            preparation.ExpiresAt,
            preparation.TrainerName,
            preparation.TrainerEmail,
            preparation.TrainerCultureName,
            preparation.TrainerTimeZone), cancellationToken);
    }
}
