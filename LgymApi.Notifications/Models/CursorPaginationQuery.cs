using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications.Models;

public sealed record CursorPaginationQuery(
    int Limit = 20,
    DateTimeOffset? CursorCreatedAt = null,
    Id<InAppNotification>? CursorId = null);
