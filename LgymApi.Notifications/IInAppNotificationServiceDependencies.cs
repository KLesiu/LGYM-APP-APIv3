using LgymApi.Application.Repositories;

namespace LgymApi.Application.Notifications;

internal interface IInAppNotificationServiceDependencies
{
    IInAppNotificationRepository InAppNotificationRepository { get; }
    IUnitOfWork UnitOfWork { get; }
    IInAppNotificationPushPublisher PushPublisher { get; }
    INotificationEventBridge NotificationEventBridge { get; }
}

internal sealed record InAppNotificationServiceDependencies : IInAppNotificationServiceDependencies
{
    public IInAppNotificationRepository InAppNotificationRepository { get; }
    public IUnitOfWork UnitOfWork { get; }
    public IInAppNotificationPushPublisher PushPublisher { get; }
    public INotificationEventBridge NotificationEventBridge { get; }

    public InAppNotificationServiceDependencies(
        IInAppNotificationRepository inAppNotificationRepository,
        IUnitOfWork unitOfWork,
        IInAppNotificationPushPublisher pushPublisher,
        INotificationEventBridge notificationEventBridge)
    {
        InAppNotificationRepository = inAppNotificationRepository;
        UnitOfWork = unitOfWork;
        PushPublisher = pushPublisher;
        NotificationEventBridge = notificationEventBridge;
    }
}
