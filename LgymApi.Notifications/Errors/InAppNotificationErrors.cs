using LgymApi.Application.BuildingBlocks.Errors;

namespace LgymApi.Application.Notifications.Errors;

public sealed class InAppNotificationNotFoundError : NotFoundError
{
    public InAppNotificationNotFoundError() : base("Notification not found") { }
}

public sealed class InAppNotificationForbiddenError : ForbiddenError
{
    public InAppNotificationForbiddenError() : base("Notification forbidden") { }
}
