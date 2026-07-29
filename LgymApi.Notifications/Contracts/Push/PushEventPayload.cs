using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications.Contracts.Push;

public sealed record PushEventPayload(
    int SchemaVersion,
    string Type,
    string EventId,
    string? EntityId,
    Id<InAppNotification>? InAppNotificationId,
    string? Deeplink);
