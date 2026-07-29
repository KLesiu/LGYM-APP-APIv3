using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications.Models;

public sealed record EnqueueNotificationEventInput(
    Id<User> UserId,
    int SchemaVersion,
    string Type,
    string EventKey,
    string? EntityKey,
    Id<InAppNotification>? InAppNotificationId,
    string? Deeplink);
