using LgymApi.Domain.ValueObjects;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations.Accept;

public sealed record AcceptInvitationCommand(
    Id<UserEntity> TraineeId,
    Id<TrainerInvitationEntity> InvitationId);
