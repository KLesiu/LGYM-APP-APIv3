using LgymApi.Application.Notifications.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications;

public interface IInAppNotificationWireWriter
{
    Task CreateAsync(string recipientId, string actorId, string deliveryKey, string message, string redirectUrl, string notificationType, CancellationToken cancellationToken = default);
}

internal sealed class InAppNotificationWireWriter(IInAppNotificationService notificationService) : IInAppNotificationWireWriter
{
    public async Task CreateAsync(string recipientId, string actorId, string deliveryKey, string message, string redirectUrl, string notificationType, CancellationToken cancellationToken = default)
    {
        if (!Id<User>.TryParse(recipientId, out var recipient) || !Id<User>.TryParse(actorId, out var actor))
        {
            throw new ArgumentException("In-app notification wire IDs are invalid.");
        }

        if (!InAppNotificationTypes.TryFromValue(notificationType, out var type))
        {
            throw new ArgumentException("In-app notification type is invalid.", nameof(notificationType));
        }

        var result = await notificationService.CreateAsync(new CreateInAppNotificationInput(recipient, actor, deliveryKey, false, message, redirectUrl, type), cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error.ToString());
        }
    }
}
