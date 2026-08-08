namespace LgymApi.ArchitectureTests;

internal enum PersistedCommandAuthorizationClass
{
    CommittedSystemIntent
}

internal sealed record PersistedCommandAuthorizationCatalogEntry(
    string CanonicalId,
    string RuntimeCommandType,
    string Owner,
    IReadOnlyList<string> SubjectIds,
    IReadOnlyList<string> RecipientIds,
    IReadOnlyList<string> ScheduleSites);

internal static class PersistedCommandAuthorizationCatalog
{
    public const PersistedCommandAuthorizationClass AuthorizationClass =
        PersistedCommandAuthorizationClass.CommittedSystemIntent;

    public static IReadOnlyList<PersistedCommandAuthorizationCatalogEntry> Entries { get; } =
    [
        Entry("UserRegisteredCommand", "Identity & Accounts", ["UserId"], ["UserId"], ["LgymApi.Identity/Registration/UserRegistrationService.cs"]),
        Entry("TrainingCompletedCommand", "Workout & Progress", ["TrainingId", "UserId"], ["UserId"], ["LgymApi.Infrastructure/Repositories/WorkoutProgress/WorkoutTrainingPersistenceRepository.cs"]),
        Entry("InvitationCreatedCommand", "Coaching", ["InvitationId"], ["InvitationId"], ["LgymApi.Application/Coaching/Invitations/Create/CreateInvitationUseCase.cs", "LgymApi.Application/Coaching/Invitations/CreateByEmail/CreateInvitationByEmailUseCase.cs"]),
        Entry("InvitationAcceptedCommand", "Coaching", ["InvitationId"], ["InvitationId"], ["LgymApi.Application/Coaching/Invitations/Accept/AcceptInvitationUseCase.cs"]),
        Entry("InvitationRevokedCommand", "Coaching", ["InvitationId"], ["InvitationId"], ["LgymApi.Application/Coaching/Invitations/Revoke/RevokeInvitationUseCase.cs"]),
        Entry("DietPlanUpdatedInAppNotificationCommand", "Nutrition", ["DietPlanId", "TrainerId"], ["TraineeId"], ["LgymApi.Application/Nutrition/DietPlans/CreateTraineePlan/CreateTraineeDietPlanUseCase.cs", "LgymApi.Application/Nutrition/DietPlans/UpdateTraineePlan/UpdateTraineeDietPlanUseCase.cs", "LgymApi.Application/Nutrition/DietPlans/ActivateTraineePlan/ActivateTraineeDietPlanUseCase.cs"]),
        Entry("TraineeNoteUpdatedInAppNotificationCommand", "Coaching", ["TraineeNoteId", "TrainerId"], ["TraineeId"], ["LgymApi.Application/Coaching/TraineeNotes/Create/CreateTraineeNoteUseCase.cs", "LgymApi.Application/Coaching/TraineeNotes/Update/UpdateTraineeNoteUseCase.cs"]),
        Entry("ReportSubmissionCreatedInAppNotificationCommand", "Reporting", ["SubmissionId", "TraineeId"], ["TrainerId"], ["LgymApi.Application/Features/Reporting/ReportingService.Submissions.cs"]),
        Entry("ReportSubmissionAcceptedProgressCommand", "Reporting", ["Event.ReportSubmissionId", "Event.TraineeId"], ["Event.TraineeId"], ["LgymApi.Application/Features/Reporting/ReportSubmissionAcceptedProgressCommandFactory.cs"]),
        Entry("ReportRequestCreatedInAppNotificationCommand", "Reporting", ["RequestId", "TrainerId"], ["TraineeId"], ["LgymApi.Application/Features/Reporting/ReportingService.Requests.cs", "LgymApi.Application/Features/Reporting/RecurringReportAssignmentService.RequestNow.cs"]),
        Entry("ReportFeedbackAddedInAppNotificationCommand", "Reporting", ["SubmissionId", "TrainerId"], ["TraineeId"], ["LgymApi.Application/Features/Reporting/ReportingService.Submissions.cs"]),
        Entry("TrainerInvitationAcceptedInAppNotificationCommand", "Coaching", ["InvitationId", "TraineeId"], ["TrainerId"], ["LgymApi.Application/Coaching/Invitations/Accept/AcceptInvitationUseCase.cs"]),
        Entry("TrainerInvitationCreatedInAppNotificationCommand", "Coaching", ["InvitationId", "TrainerId"], ["TraineeId"], ["LgymApi.Application/Coaching/Invitations/Create/CreateInvitationUseCase.cs", "LgymApi.Application/Coaching/Invitations/CreateByEmail/CreateInvitationByEmailUseCase.cs"]),
        Entry("TrainerInvitationRejectedInAppNotificationCommand", "Coaching", ["InvitationId", "TrainerId"], ["TraineeId"], ["LgymApi.Application/Coaching/Invitations/Reject/RejectInvitationUseCase.cs"]),
        Entry("TrainerRelationshipEndedInAppNotificationCommand", "Coaching", ["TrainerId"], ["TraineeId"], ["LgymApi.Application/Coaching/Relationships/DetachFromTrainer/DetachFromTrainerUseCase.cs"])
    ];

    private static PersistedCommandAuthorizationCatalogEntry Entry(
        string commandName,
        string owner,
        IReadOnlyList<string> subjectIds,
        IReadOnlyList<string> recipientIds,
        IReadOnlyList<string> scheduleSites) =>
        new(
            $"LgymApi.BackgroundWorker.Common.Commands.{commandName}",
            commandName,
            owner,
            subjectIds,
            recipientIds,
            scheduleSites);
}
