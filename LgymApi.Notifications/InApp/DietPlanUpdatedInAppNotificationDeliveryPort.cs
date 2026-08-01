namespace LgymApi.Application.Notifications.Contracts.InApp;

public sealed record DietPlanUpdatedInAppNotificationDeliveryRequest(
    string DietPlanId,
    string TraineeId,
    string TrainerId,
    string DietPlanName,
    DateTimeOffset TriggeredAt);

public interface IDietPlanUpdatedInAppNotificationDeliveryPort
{
    Task DeliverAsync(DietPlanUpdatedInAppNotificationDeliveryRequest request, CancellationToken cancellationToken = default);
}

public interface IDietPlanUpdatedActionExecutionPort
{
    Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default);
}
