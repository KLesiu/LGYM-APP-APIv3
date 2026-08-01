using LgymApi.BackgroundWorker.Actions;
using ApplicationActionCommand = LgymApi.Application.Platform.Contracts.BackgroundCommands.IActionCommand;
using ApplicationDietPlanUpdatedCommand = LgymApi.Application.Nutrition.Contracts.BackgroundCommands.DietPlanUpdatedInAppNotificationCommand;
using ApplicationInvitationAcceptedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationAcceptedCommand;
using ApplicationInvitationCreatedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationCreatedCommand;
using ApplicationInvitationRevokedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.InvitationRevokedCommand;
using ApplicationReportFeedbackAddedCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportFeedbackAddedInAppNotificationCommand;
using ApplicationReportRequestCreatedCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportRequestCreatedInAppNotificationCommand;
using ApplicationReportSubmissionAcceptedProgressCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionAcceptedProgressCommand;
using ApplicationReportSubmissionCreatedCommand = LgymApi.Application.Reporting.Contracts.BackgroundCommands.ReportSubmissionCreatedInAppNotificationCommand;
using ApplicationTraineeNoteUpdatedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.TraineeNoteUpdatedInAppNotificationCommand;
using ApplicationTrainerInvitationAcceptedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationAcceptedInAppNotificationCommand;
using ApplicationTrainerInvitationCreatedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationCreatedInAppNotificationCommand;
using ApplicationTrainerInvitationRejectedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerInvitationRejectedInAppNotificationCommand;
using ApplicationTrainerRelationshipEndedCommand = LgymApi.Application.Coaching.Contracts.BackgroundCommands.TrainerRelationshipEndedInAppNotificationCommand;
using ApplicationTrainingCompletedCommand = LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands.TrainingCompletedCommand;
using ApplicationUserRegisteredCommand = LgymApi.Application.Identity.Contracts.BackgroundCommands.UserRegisteredCommand;

namespace LgymApi.BackgroundWorker.Runtime;

public sealed partial class CommandContractRegistry
{
    private static CommandContract[] CreateDefaultContracts() =>
    [
        Create<ApplicationUserRegisteredCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.UserRegisteredCommand",
            typeof(SendRegistrationEmailHandler)),
        Create<ApplicationTrainingCompletedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.TrainingCompletedCommand",
            typeof(TrainingCompletedEmailCommandHandler),
            typeof(UpdateTrainingMainRecordsHandler)),
        Create<ApplicationInvitationCreatedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.InvitationCreatedCommand",
            typeof(SendInvitationEmailHandler)),
        Create<ApplicationInvitationAcceptedCommand>(
            InvitationAcceptedCanonicalId,
            typeof(InvitationAcceptedEmailHandler)),
        Create<ApplicationInvitationRevokedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.InvitationRevokedCommand",
            typeof(InvitationRevokedEmailHandler)),
        Create<ApplicationDietPlanUpdatedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.DietPlanUpdatedInAppNotificationCommand",
            typeof(DietPlanUpdatedInAppNotificationCommandHandler)),
        Create<ApplicationTraineeNoteUpdatedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.TraineeNoteUpdatedInAppNotificationCommand",
            typeof(TraineeNoteUpdatedInAppNotificationCommandHandler)),
        Create<ApplicationReportSubmissionCreatedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionCreatedInAppNotificationCommand",
            typeof(ReportSubmissionCreatedInAppNotificationCommandHandler)),
        Create<ApplicationReportSubmissionAcceptedProgressCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.ReportSubmissionAcceptedProgressCommand",
            typeof(ReportSubmissionAcceptedProgressCommandHandler)),
        Create<ApplicationReportRequestCreatedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.ReportRequestCreatedInAppNotificationCommand",
            typeof(ReportRequestCreatedInAppNotificationCommandHandler)),
        Create<ApplicationReportFeedbackAddedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.ReportFeedbackAddedInAppNotificationCommand",
            typeof(ReportFeedbackAddedInAppNotificationCommandHandler)),
        Create<ApplicationTrainerInvitationAcceptedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationAcceptedInAppNotificationCommand",
            typeof(TrainerInvitationAcceptedInAppNotificationCommandHandler)),
        Create<ApplicationTrainerInvitationCreatedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationCreatedInAppNotificationCommand",
            typeof(TrainerInvitationCreatedInAppNotificationCommandHandler)),
        Create<ApplicationTrainerInvitationRejectedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.TrainerInvitationRejectedInAppNotificationCommand",
            typeof(TrainerInvitationRejectedInAppNotificationCommandHandler)),
        Create<ApplicationTrainerRelationshipEndedCommand>(
            "LgymApi.BackgroundWorker.Common.Commands.TrainerRelationshipEndedInAppNotificationCommand",
            typeof(TrainerRelationshipEndedInAppNotificationCommandHandler))
    ];

    private static CommandContract Create<TCommand>(string canonicalId, params Type[] handlerTypes)
        where TCommand : ApplicationActionCommand =>
        new(canonicalId, typeof(TCommand), typeof(TCommand).FullName!, handlerTypes);
}
