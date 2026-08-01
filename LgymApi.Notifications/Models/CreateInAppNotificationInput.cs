using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications.Models;

public sealed record CreateInAppNotificationInput(
    Id<User> RecipientId,
    Id<User>? SenderUserId,
    string? DeliveryKey,
    bool IsSystemNotification,
    string Message,
    string? RedirectUrl,
    InAppNotificationType Type);
