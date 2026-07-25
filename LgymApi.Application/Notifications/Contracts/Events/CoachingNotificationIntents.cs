using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Notifications.Contracts.Events;

public enum CoachingNotificationLegacyChannel
{
    Email,
    InApp
}

public abstract record CoachingNotificationIntent(CoachingNotificationLegacyChannel EligibleLegacyChannel);

public sealed record InvitationCreatedCoachingNotificationIntent(
    CoachingNotificationLegacyChannel EligibleLegacyChannel,
    Id<TrainerInvitation> InvitationId,
    Id<User> TrainerId,
    Id<User>? TraineeId,
    string InviteeEmail,
    string InvitationCode,
    DateTimeOffset ExpiresAt,
    AccountReadModel? Trainer,
    AccountReadModel? Trainee) : CoachingNotificationIntent(EligibleLegacyChannel);

public sealed record InvitationAcceptedCoachingNotificationIntent(
    CoachingNotificationLegacyChannel EligibleLegacyChannel,
    Id<TrainerInvitation> InvitationId,
    Id<User> TrainerId,
    Id<User> TraineeId,
    AccountReadModel? Trainer,
    AccountReadModel? Trainee) : CoachingNotificationIntent(EligibleLegacyChannel);

public sealed record InvitationRejectedCoachingNotificationIntent(
    CoachingNotificationLegacyChannel EligibleLegacyChannel,
    Id<TrainerInvitation> InvitationId,
    Id<User> TrainerId,
    Id<User> TraineeId) : CoachingNotificationIntent(EligibleLegacyChannel);

public sealed record InvitationRevokedCoachingNotificationIntent(
    CoachingNotificationLegacyChannel EligibleLegacyChannel,
    Id<TrainerInvitation> InvitationId,
    Id<User> TrainerId,
    string InviteeEmail,
    AccountReadModel? Trainer) : CoachingNotificationIntent(EligibleLegacyChannel);

public sealed record RelationshipEndedCoachingNotificationIntent(
    CoachingNotificationLegacyChannel EligibleLegacyChannel,
    Id<User> TrainerId,
    Id<User> TraineeId,
    AccountReadModel? Trainer,
    AccountReadModel? Trainee) : CoachingNotificationIntent(EligibleLegacyChannel);

public sealed record TraineeNoteUpdatedCoachingNotificationIntent(
    CoachingNotificationLegacyChannel EligibleLegacyChannel,
    Id<TraineeNote> TraineeNoteId,
    Id<User> TraineeId,
    Id<User> TrainerId,
    string? NoteTitle,
    DateTimeOffset TriggeredAt,
    AccountReadModel? Trainer,
    AccountReadModel? Trainee) : CoachingNotificationIntent(EligibleLegacyChannel);

public enum CoachingEmailSchedulingKind
{
    InvitationCreated,
    InvitationAccepted,
    InvitationRevoked
}

public sealed record CoachingEmailSchedulingRequest(
    CoachingEmailSchedulingKind Kind,
    EmailNotificationType NotificationType,
    Id<TrainerInvitation> InvitationId,
    Id<CorrelationScope> CorrelationId,
    string RecipientEmail,
    string CultureName,
    string PreferredTimeZone,
    string TrainerName,
    string? TraineeName,
    string? InvitationCode,
    DateTimeOffset? ExpiresAt);

public sealed record CoachingNotificationIntentResult(
    CoachingEmailSchedulingRequest? EmailSchedulingRequest,
    AppError? InAppError);
