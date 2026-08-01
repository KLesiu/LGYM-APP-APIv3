using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications.Models;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    bool HasNextPage,
    DateTimeOffset? NextCursorCreatedAt,
    Id<InAppNotification>? NextCursorId);
