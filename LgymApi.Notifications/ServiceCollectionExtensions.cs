using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Application.Notifications;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Application.Features.PasswordReset.Contracts;
using LgymApi.Application.Notifications.Adapters;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Notifications.Providers.Fcm;
using LgymApi.Application.Repositories;
using LgymApi.Notifications.ApiAdapters;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.Services;
using LgymApi.Notifications;
using LgymApi.Notifications.Persistence;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Notifications.Email;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Notifications.InApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<IInAppNotificationService, InAppNotificationService>();
        services.AddScoped<IInAppNotificationWireWriter, InAppNotificationWireWriter>();
        services.AddScoped<ICoachingNotificationIntentService, CoachingNotificationIntentService>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();
        services.AddScoped<IPushNotificationDeliveryService, PushNotificationDeliveryService>();
        services.AddScoped<IStalePushInstallationCleanupService, StalePushInstallationCleanupService>();
        services.AddScoped<IPushInstallationLifecycleService, PushInstallationLifecycleService>();
        services.AddScoped<IAccountSessionDisassociationPort, PushInstallationSessionDisassociationAdapter>();
        services.AddScoped<INotificationEventBridge, NotificationEventBridge>();
        services.AddScoped<IPushInstallationRepository, PushInstallationRepository>();
        services.AddScoped<IPushNotificationMessageRepository, PushNotificationMessageRepository>();
        services.AddScoped<IInAppNotificationRepository, InAppNotificationRepository>();
        services.AddScoped<IEmailNotificationLogRepository>(static provider =>
            new EmailNotificationLogRepository(
                provider.GetRequiredService<INotificationsPersistenceContext>(),
                provider.GetRequiredService<IEmailNotificationLeaseSettings>()));
        services.AddScoped<IEmailNotificationSubscriptionRepository, EmailNotificationSubscriptionRepository>();
        services.AddScoped<IEmailJobExecutionPort, EmailJobHandlerService>();
        services.AddScoped(typeof(IEmailSchedulingPort<>), typeof(EmailSchedulerService<>));
        services.AddScoped<ITrainingCompletedEmailDeliveryPort, TrainingCompletedEmailDeliveryService>();
        services.AddScoped<IWelcomeEmailDeliveryPort, WelcomeEmailDeliveryService>();
        services.AddScoped<IInvitationCreatedEmailDeliveryPort, InvitationCreatedEmailDeliveryService>();
        services.AddScoped<IInvitationAcceptedEmailDeliveryPort, InvitationAcceptedEmailDeliveryService>();
        services.AddScoped<IInvitationRevokedEmailDeliveryPort, InvitationRevokedEmailDeliveryService>();
        services.AddScoped<ICoachingEmailNotificationFeature, CoachingEmailNotificationSchedulerAdapter>();
        services.AddScoped<ICoachingEmailNotificationScheduler, CoachingEmailNotificationSchedulerAdapter>();
        services.AddScoped<IPasswordRecoveryEmailScheduler, PasswordRecoveryEmailSchedulerAdapter>();
        services.AddScoped<IUserRegisteredActionExecutionPort, UserRegisteredActionExecutionPort>();
        services.AddScoped<IDietPlanUpdatedInAppNotificationDeliveryPort, DietPlanUpdatedInAppNotificationDeliveryService>();
        services.AddScoped<IDietPlanUpdatedActionExecutionPort, DietPlanUpdatedActionExecutionPort>();
        services.AddScoped<ITrainerInvitationCreatedInAppDeliveryPort, TrainerInvitationCreatedInAppDeliveryService>();
        services.AddScoped<ITraineeNoteUpdatedInAppDeliveryPort, TraineeNoteUpdatedInAppDeliveryService>();
        services.AddScoped<IRelationshipEndedDeliveryPort, RelationshipEndedDeliveryService>();
        services.AddScoped<ITrainerInvitationAcceptedInAppDeliveryPort, TrainerInvitationAcceptedInAppDeliveryService>();
        services.AddScoped<ITrainerInvitationRejectedInAppDeliveryPort, TrainerInvitationRejectedInAppDeliveryService>();
        services.AddScoped<IReportRequestCreatedActionExecutionPort, ReportRequestCreatedActionExecutionPort>();
        services.AddScoped<IReportFeedbackAddedActionExecutionPort, ReportFeedbackAddedActionExecutionPort>();
        services.AddScoped<IReportSubmissionCreatedActionExecutionPort, ReportSubmissionCreatedActionExecutionPort>();

        return services;
    }

    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var pushNotificationOptions = PushNotificationOptionsFactory.Create(configuration);
        PushNotificationOptionsFactory.Validate(pushNotificationOptions);
        EmailServiceCollectionExtensions.ValidateEmailConfiguration(configuration);

        services.AddNotificationsModule();
        services.AddEmailServices(configuration);

        services.AddHttpClient(nameof(FcmPushSender));
        services.AddSingleton(pushNotificationOptions);
        services.AddSingleton<IStalePushInstallationCleanupSettings, PushInstallationCleanupSettings>();
        services.AddSingleton<IPushNotificationDeliveryRetrySettings, PushNotificationDeliveryRetrySettings>();
        services.AddScoped<IPushProviderSender, FcmPushSender>();

        return services;
    }

    public static IServiceCollection AddNotificationsApiAdapters(this IServiceCollection services)
    {
        services.AddScoped<IInAppNotificationApiAdapter, InAppNotificationApiAdapter>();
        services.AddScoped<INotificationEventApiAdapter, NotificationEventApiAdapter>();
        services.AddScoped<IPushInstallationApiAdapter, PushInstallationApiAdapter>();

        return services;
    }
}
