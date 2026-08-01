using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.Application.Platform.Contracts.Serialization;
using System.Text.Json;

namespace LgymApi.BackgroundWorker.Actions;

public sealed partial class DietPlanUpdatedInAppNotificationCommandHandler : IBackgroundAction<DietPlanUpdatedInAppNotificationCommand>
{
    private readonly IDietPlanUpdatedActionExecutionPort _port;

    public DietPlanUpdatedInAppNotificationCommandHandler(
        IDietPlanUpdatedActionExecutionPort port)
    {
        _port = port;
    }

    public async Task ExecuteAsync(DietPlanUpdatedInAppNotificationCommand command, CancellationToken cancellationToken = default)
    {
        await _port.ExecuteAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
    }
}
