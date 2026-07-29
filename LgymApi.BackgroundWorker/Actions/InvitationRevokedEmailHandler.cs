using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Platform.Contracts.Serialization;
using System.Text.Json;

namespace LgymApi.BackgroundWorker.Actions;

public sealed class InvitationRevokedEmailHandler(IInvitationRevokedEmailPreparationPort preparationPort, IInvitationRevokedEmailDeliveryPort deliveryPort) : global::LgymApi.BackgroundWorker.Actions.Contracts.IBackgroundAction<InvitationRevokedCommand>
{
    public async Task ExecuteAsync(InvitationRevokedCommand command, CancellationToken cancellationToken = default)
    {
        var preparation = await preparationPort.PrepareAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
        if (preparation is null) return;
        await deliveryPort.DeliverAsync(new InvitationRevokedEmailDeliveryRequest(preparation.InvitationId, preparation.TrainerId, preparation.InviteeEmail, preparation.TrainerEmail, preparation.TrainerCultureName, preparation.TrainerTimeZone, preparation.TrainerName), cancellationToken);
    }
}
