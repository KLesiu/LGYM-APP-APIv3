using System.Text.Json;
using LgymApi.Application.Notifications.Contracts.InApp;

namespace LgymApi.Application.Notifications.InApp;

internal sealed class DietPlanUpdatedActionExecutionPort(
    IDietPlanUpdatedInAppNotificationDeliveryPort deliveryPort) : IDietPlanUpdatedActionExecutionPort
{
    public Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        return deliveryPort.DeliverAsync(new DietPlanUpdatedInAppNotificationDeliveryRequest(
            root.GetProperty("dietPlanId").GetString() ?? string.Empty,
            root.GetProperty("traineeId").GetString() ?? string.Empty,
            root.GetProperty("trainerId").GetString() ?? string.Empty,
            root.GetProperty("dietPlanName").GetString() ?? string.Empty,
            root.GetProperty("triggeredAt").GetDateTimeOffset()), cancellationToken);
    }
}
