using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Platform.Contracts.Serialization;
using System.Text.Json;

namespace LgymApi.BackgroundWorker.Actions;

public sealed class InvitationAcceptedEmailHandler(
    IInvitationAcceptedEmailPreparationPort preparationPort,
    IInvitationAcceptedEmailDeliveryPort deliveryPort) : global::LgymApi.BackgroundWorker.Actions.Contracts.IBackgroundAction<InvitationAcceptedCommand>
{
    public async Task ExecuteAsync(InvitationAcceptedCommand command, CancellationToken cancellationToken = default)
    {
        var preparation = await preparationPort.PrepareAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
        if (preparation is null) return;
        await deliveryPort.DeliverAsync(new InvitationAcceptedEmailDeliveryRequest(
            preparation.InvitationId, preparation.TrainerId, preparation.TraineeId, preparation.TrainerEmail,
            preparation.TrainerCultureName, preparation.TrainerTimeZone, preparation.TrainerName, preparation.TraineeName), cancellationToken);
    }
}
