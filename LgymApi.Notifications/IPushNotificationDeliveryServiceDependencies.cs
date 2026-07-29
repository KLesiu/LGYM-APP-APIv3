using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Repositories;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications;

internal interface IPushNotificationDeliveryServiceDependencies
{
    IPushNotificationMessageRepository PushNotificationMessageRepository { get; }
    IPushInstallationRepository PushInstallationRepository { get; }
    IPushProviderSender PushProviderSender { get; }
    IPushBackgroundScheduler PushBackgroundScheduler { get; }
    IPushNotificationDeliveryRetrySettings RetrySettings { get; }
    IUnitOfWork UnitOfWork { get; }
    ILogger<PushNotificationDeliveryService> Logger { get; }
}

internal sealed class PushNotificationDeliveryServiceDependencies : IPushNotificationDeliveryServiceDependencies
{
    public IPushNotificationMessageRepository PushNotificationMessageRepository { get; }
    public IPushInstallationRepository PushInstallationRepository { get; }
    public IPushProviderSender PushProviderSender { get; }
    public IPushBackgroundScheduler PushBackgroundScheduler { get; }
    public IPushNotificationDeliveryRetrySettings RetrySettings { get; }
    public IUnitOfWork UnitOfWork { get; }
    public ILogger<PushNotificationDeliveryService> Logger { get; }

    public PushNotificationDeliveryServiceDependencies(
        IPushNotificationMessageRepository pushNotificationMessageRepository,
        IPushInstallationRepository pushInstallationRepository,
        IPushProviderSender pushProviderSender,
        IPushBackgroundScheduler pushBackgroundScheduler,
        IPushNotificationDeliveryRetrySettings retrySettings,
        IUnitOfWork unitOfWork,
        ILogger<PushNotificationDeliveryService> logger)
    {
        PushNotificationMessageRepository = pushNotificationMessageRepository ?? throw new ArgumentNullException(nameof(pushNotificationMessageRepository));
        PushInstallationRepository = pushInstallationRepository ?? throw new ArgumentNullException(nameof(pushInstallationRepository));
        PushProviderSender = pushProviderSender ?? throw new ArgumentNullException(nameof(pushProviderSender));
        PushBackgroundScheduler = pushBackgroundScheduler ?? throw new ArgumentNullException(nameof(pushBackgroundScheduler));
        RetrySettings = retrySettings ?? throw new ArgumentNullException(nameof(retrySettings));
        UnitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
}
