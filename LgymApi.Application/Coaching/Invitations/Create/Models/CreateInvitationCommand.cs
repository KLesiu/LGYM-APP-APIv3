using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations.Create;

public sealed record CreateInvitationCommand(Id<UserEntity> TrainerId, Id<UserEntity> TraineeId);
