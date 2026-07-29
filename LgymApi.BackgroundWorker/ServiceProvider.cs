using LgymApi.BackgroundWorker.Actions;
using LgymApi.BackgroundWorker.Common;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.BackgroundWorker.Common.Jobs;
using LgymApi.BackgroundWorker.Jobs;
using LgymApi.BackgroundWorker.Push;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.BackgroundWorker.Notifications;
using LgymApi.Application.Features.PasswordReset.Contracts;
using LgymApi.Infrastructure.Jobs;
using LgymApi.Infrastructure.Services;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.Application.Notifications.Contracts.Push;
using LgymApi.BackgroundWorker.Services;
using LgymApi.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ApplicationCommandDispatcher = LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandDispatcher;
using ApplicationCommandOutboxWriter = LgymApi.Application.Platform.Contracts.BackgroundCommands.ICommandOutboxWriter;
using UserRegisteredCommand = LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand;
using TrainingCompletedCommand = LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands.TrainingCompletedCommand;
using InvitationCreatedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationCreatedCommand;
using InvitationAcceptedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationAcceptedCommand;
using InvitationRevokedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationRevokedCommand;
using DietPlanUpdatedInAppNotificationCommand = LgymApi.Application.Nutrition.Contracts.BackgroundCommands.DietPlanUpdatedInAppNotificationCommand;
using TraineeNoteUpdatedInAppNotificationCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.TraineeNoteUpdatedInAppNotificationCommand;
using ReportSubmissionCreatedInAppNotificationCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionCreatedInAppNotificationCommand;
using ReportSubmissionAcceptedProgressCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionAcceptedProgressCommand;
using ReportRequestCreatedInAppNotificationCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportRequestCreatedInAppNotificationCommand;
using ReportFeedbackAddedInAppNotificationCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportFeedbackAddedInAppNotificationCommand;
using TrainerInvitationAcceptedInAppNotificationCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationAcceptedInAppNotificationCommand;
using TrainerInvitationCreatedInAppNotificationCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationCreatedInAppNotificationCommand;
using TrainerInvitationRejectedInAppNotificationCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationRejectedInAppNotificationCommand;
using TrainerRelationshipEndedInAppNotificationCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerRelationshipEndedInAppNotificationCommand;

namespace LgymApi.BackgroundWorker;

public static class ServiceProvider
{
    public static IServiceCollection AddBackgroundWorkerServices(
        this IServiceCollection services,
        bool isTesting,
        bool hostBackgroundServer = false)
    {
        var commandContractRegistry = CommandContractRegistry.CreateDefault();
        services.AddSingleton(commandContractRegistry);

        if (!isTesting && hostBackgroundServer)
        {
            services.AddInfrastructureBackgroundServer();
        }

        if (isTesting)
        {
            services.AddScoped<IEmailBackgroundScheduler, NoOpEmailBackgroundScheduler>();
        }
        else
        {
            services.AddScoped<IEmailBackgroundScheduler, HangfireEmailBackgroundScheduler>();
        }

        if (isTesting)
        {
            services.AddScoped<IActionMessageScheduler, NoOpActionMessageScheduler>();
        }
        else
        {
            services.AddScoped<IActionMessageScheduler, HangfireActionMessageScheduler>();
        }

        if (isTesting)
        {
            services.AddScoped<IPushBackgroundScheduler, Services.NoOpPushBackgroundScheduler>();
        }
        else
        {
            services.AddScoped<IPushBackgroundScheduler, Services.HangfirePushBackgroundScheduler>();
        }

        services.AddScoped<IEmailScheduler<InvitationEmailPayload>, EmailSchedulerService<InvitationEmailPayload>>();
        services.AddScoped<IEmailScheduler<InvitationAcceptedEmailPayload>, EmailSchedulerService<InvitationAcceptedEmailPayload>>();
        services.AddScoped<IEmailScheduler<InvitationRevokedEmailPayload>, EmailSchedulerService<InvitationRevokedEmailPayload>>();
        services.AddScoped<IEmailScheduler<TrainingCompletedEmailPayload>, EmailSchedulerService<TrainingCompletedEmailPayload>>();
        services.AddScoped<IEmailScheduler<WelcomeEmailPayload>, EmailSchedulerService<WelcomeEmailPayload>>();
        services.AddScoped<IEmailScheduler<PasswordRecoveryEmailPayload>, EmailSchedulerService<PasswordRecoveryEmailPayload>>();
        services.AddScoped<IEmailJobHandler, EmailJobHandlerService>();
        services.AddScoped<ApplicationCommandDispatcher, CommandDispatcher>();
        services.AddScoped<ApplicationCommandOutboxWriter, CommandOutboxWriter>();
        services.AddScoped<Runtime.IBackgroundActionResolver, BackgroundActionResolver>();

        services.AddScoped<IInvitationEmailJob, InvitationEmailJob>();
        services.AddScoped<IWelcomeEmailJob, WelcomeEmailJob>();
        services.AddScoped<IEmailJob, EmailJob>();
        services.AddScoped<IPushNotificationJob, PushNotificationJob>();

        services.AddScoped<InvitationEmailJob>();
        services.AddScoped<WelcomeEmailJob>();
        services.AddScoped<EmailJob>();
        services.AddScoped<PushNotificationJob>();
        services.AddScoped<IActionMessageJob, ActionMessageJob>();
        services.AddScoped<ActionMessageJob>();
        services.AddScoped<ICommittedIntentDispatchJob, CommittedIntentDispatchJob>();
        services.AddScoped<IExpiredPhotoUploadCleanupJob, ExpiredPhotoUploadCleanupJob>();
        services.AddScoped<IRecurringReportAssignmentProcessingJob, RecurringReportAssignmentProcessingJob>();
        services.AddScoped<IStalePushInstallationCleanupJob, StalePushInstallationCleanupJob>();

        services.AddScoped<BackgroundActionOrchestratorService>();
        services.AddScoped<PushNotificationJobHandlerService>();
        services.AddScoped<StalePushInstallationCleanupJob>();

        // Register typed background action handlers
        services.AddBackgroundAction<UserRegisteredCommand, SendRegistrationEmailHandler>();
        services.AddBackgroundAction<InvitationCreatedCommand, SendInvitationEmailHandler>();
        services.AddBackgroundAction<TrainingCompletedCommand, TrainingCompletedEmailCommandHandler>();
        services.AddBackgroundAction<TrainingCompletedCommand, UpdateTrainingMainRecordsHandler>();
        services.AddBackgroundAction<InvitationAcceptedCommand, InvitationAcceptedEmailHandler>();
        services.AddBackgroundAction<InvitationRevokedCommand, InvitationRevokedEmailHandler>();
        services.AddBackgroundAction<TrainerInvitationCreatedInAppNotificationCommand, TrainerInvitationCreatedInAppNotificationCommandHandler>();
        services.AddBackgroundAction<TrainerInvitationAcceptedInAppNotificationCommand, TrainerInvitationAcceptedInAppNotificationCommandHandler>();
        services.AddBackgroundAction<TrainerInvitationRejectedInAppNotificationCommand, TrainerInvitationRejectedInAppNotificationCommandHandler>();
        services.AddBackgroundAction<ReportRequestCreatedInAppNotificationCommand, ReportRequestCreatedInAppNotificationCommandHandler>();
        services.AddBackgroundAction<ReportSubmissionCreatedInAppNotificationCommand, ReportSubmissionCreatedInAppNotificationCommandHandler>();
        services.AddBackgroundAction<ReportSubmissionAcceptedProgressCommand, ReportSubmissionAcceptedProgressCommandHandler>();
        services.AddBackgroundAction<ReportFeedbackAddedInAppNotificationCommand, ReportFeedbackAddedInAppNotificationCommandHandler>();
        services.AddBackgroundAction<DietPlanUpdatedInAppNotificationCommand, DietPlanUpdatedInAppNotificationCommandHandler>();
        services.AddBackgroundAction<TraineeNoteUpdatedInAppNotificationCommand, TraineeNoteUpdatedInAppNotificationCommandHandler>();
        services.AddBackgroundAction<TrainerRelationshipEndedInAppNotificationCommand, TrainerRelationshipEndedInAppNotificationCommandHandler>();

        BackgroundActionRegistrationValidator.Validate(services, commandContractRegistry);
        return services;
    }

    /// <summary>
    /// Registers a strongly-typed background action handler for a specific command type.
    /// Handlers are resolved at runtime by exact command type only (1:1 matching).
    /// Multiple handlers can be registered for the same command type.
    /// </summary>
    /// <typeparam name="TCommand">The command type this action handles. Must implement IActionCommand.</typeparam>
    /// <typeparam name="TAction">The action handler implementation. Must implement IBackgroundAction&lt;TCommand&gt;.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection AddBackgroundAction<TCommand, TAction>(this IServiceCollection services)
        where TCommand : LgymApi.Application.Platform.Contracts.BackgroundCommands.IActionCommand
        where TAction : class, Actions.Contracts.IBackgroundAction<TCommand>
    {
        // Register typed handler for resolution by orchestrator
        services.AddScoped<Actions.Contracts.IBackgroundAction<TCommand>, TAction>();

        // Register concrete implementation for dependency graph
        services.AddScoped<TAction>();

        return services;
    }
}
