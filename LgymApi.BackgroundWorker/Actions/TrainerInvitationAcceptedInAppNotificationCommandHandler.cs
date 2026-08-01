using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Actions.Contracts;
using Microsoft.Extensions.Logging;

namespace LgymApi.BackgroundWorker.Actions;

public sealed partial class TrainerInvitationAcceptedInAppNotificationCommandHandler : IBackgroundAction<TrainerInvitationAcceptedInAppNotificationCommand>
{
    private readonly ITrainerInvitationAcceptedInAppPreparationPort _preparationPort;
    private readonly ITrainerInvitationAcceptedInAppDeliveryPort _deliveryPort;
    private readonly ILogger<TrainerInvitationAcceptedInAppNotificationCommandHandler> _logger;

    public TrainerInvitationAcceptedInAppNotificationCommandHandler(
        ITrainerInvitationAcceptedInAppPreparationPort preparationPort,
        ITrainerInvitationAcceptedInAppDeliveryPort deliveryPort,
        ILogger<TrainerInvitationAcceptedInAppNotificationCommandHandler> logger)
    {
        _preparationPort = preparationPort ?? throw new ArgumentNullException(nameof(preparationPort));
        _deliveryPort = deliveryPort ?? throw new ArgumentNullException(nameof(deliveryPort));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(TrainerInvitationAcceptedInAppNotificationCommand command, CancellationToken cancellationToken = default)
    {
        var preparation = await _preparationPort.PrepareAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
        await _deliveryPort.DeliverAsync(new TrainerInvitationAcceptedInAppDeliveryRequest(
            preparation.InvitationId, preparation.TrainerId, preparation.TraineeId), cancellationToken);
    }
}
