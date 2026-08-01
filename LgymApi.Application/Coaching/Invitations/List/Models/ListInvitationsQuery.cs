using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations.List;

public sealed record ListInvitationsQuery(Id<UserEntity> TrainerId);
