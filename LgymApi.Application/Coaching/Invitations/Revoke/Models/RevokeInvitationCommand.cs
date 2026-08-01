using LgymApi.Domain.ValueObjects;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations.Revoke;

public sealed record RevokeInvitationCommand(
    Id<UserEntity> TrainerId,
    Id<TrainerInvitationEntity> InvitationId);
