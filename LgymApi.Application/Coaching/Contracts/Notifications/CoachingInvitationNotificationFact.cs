using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Coaching.Contracts.Notifications;

public sealed record CoachingInvitationNotificationFact(
    Id<TrainerInvitation> InvitationId,
    Id<User> TrainerId,
    Id<User>? TraineeId,
    string InviteeEmail,
    string InvitationCode,
    DateTimeOffset ExpiresAt);
