using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications.Models;

public sealed record InAppNotificationResult(
    Id<InAppNotification> Id,
    Id<User> RecipientId,
    string Message,
    string? RedirectUrl,
    bool IsRead,
    InAppNotificationType Type,
    bool IsSystemNotification,
    Id<User>? SenderUserId,
    DateTimeOffset CreatedAt);
