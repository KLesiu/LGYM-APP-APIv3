using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Actions.Contracts;

namespace LgymApi.BackgroundWorker.Actions;

public sealed class TraineeNoteUpdatedInAppNotificationCommandHandler(
    ITraineeNoteUpdatedInAppPreparationPort preparationPort,
    ITraineeNoteUpdatedInAppDeliveryPort deliveryPort) : IBackgroundAction<TraineeNoteUpdatedInAppNotificationCommand>
{
    public async Task ExecuteAsync(TraineeNoteUpdatedInAppNotificationCommand command, CancellationToken cancellationToken = default)
    {
        var preparation = await preparationPort.PrepareAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
        await deliveryPort.DeliverAsync(new TraineeNoteUpdatedInAppDeliveryRequest(
            preparation.TraineeNoteId,
            preparation.TraineeId,
            preparation.TrainerId,
            preparation.NoteTitle,
            preparation.TriggeredAt,
            preparation.TrainerName,
            preparation.TrainerEmail,
            preparation.TrainerCultureName,
            preparation.TrainerTimeZone,
            preparation.TraineeName,
            preparation.TraineeEmail,
            preparation.TraineeCultureName,
            preparation.TraineeTimeZone), cancellationToken);
    }
}
