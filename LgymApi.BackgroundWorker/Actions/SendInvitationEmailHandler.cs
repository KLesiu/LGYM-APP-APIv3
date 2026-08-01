using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Platform.Contracts.Serialization;

namespace LgymApi.BackgroundWorker.Actions;

public sealed class SendInvitationEmailHandler(
    IInvitationCreatedEmailPreparationPort preparationPort,
    IInvitationCreatedEmailDeliveryPort deliveryPort) : global::LgymApi.BackgroundWorker.Actions.Contracts.IBackgroundAction<InvitationCreatedCommand>
{
    public async Task ExecuteAsync(InvitationCreatedCommand command, CancellationToken cancellationToken = default)
    {
        var preparation = await preparationPort.PrepareAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
        if (preparation is null)
        {
            return;
        }

        await deliveryPort.DeliverAsync(new InvitationCreatedEmailDeliveryRequest(
            preparation.InvitationId,
            preparation.TrainerId,
            preparation.TraineeId,
            preparation.InviteeEmail,
            preparation.InvitationCode,
            preparation.ExpiresAt,
            preparation.TrainerName,
            preparation.TrainerEmail,
            preparation.TrainerCultureName,
            preparation.TrainerTimeZone,
            preparation.TraineeName,
            preparation.TraineeEmail,
            preparation.TraineeTimeZone), cancellationToken);
    }
}
